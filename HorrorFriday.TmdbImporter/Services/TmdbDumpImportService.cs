using System.Collections.Concurrent;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.RateLimiting;
using HorrorFriday.TmdbImporter.Models;

namespace HorrorFriday.TmdbImporter.Services;

/// <summary>
/// Mode [5]: imports from the official TMDB daily dump file.
///
/// Flow:
///   1. Stream dump line-by-line; pre-filter (adult/video/popularity) — no API call
///   2. Skip IDs already processed (id <= checkpoint)
///   3. Parallel.ForEachAsync: N workers each acquire a rate-limiter token, then call the detail API
///   4. Post-filter: runtime below threshold → skip
///   5. Upsert into movies with COALESCE (never overwrites existing non-null)
///   6. Insert genres, certifications, watch providers
///   7. Checkpoint flushed to DB every DumpCheckpointEvery completions — Ctrl+C safe
/// </summary>
public sealed class TmdbDumpImportService
{
    private readonly TmdbApiService _tmdb;
    private readonly SyncDatabaseService _db;
    private readonly SyncSettings _settings;

    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    // Per-run parallel state — reset in RunAsync
    private long _linesRead;
    private long _apiCalls;
    private int _lastCompletedId;
    private int _dbCheckpoint;
    private long _completionCounter;
    private ConcurrentDictionary<int, byte> _inFlight = new();
    private readonly SemaphoreSlim _checkpointLock = new(1, 1);

    public TmdbDumpImportService(TmdbApiService tmdb, SyncDatabaseService db, SyncSettings settings)
    {
        _tmdb = tmdb;
        _db = db;
        _settings = settings;
    }

    public async Task<SyncStats> RunAsync(string dumpFilePath, CancellationToken ct)
    {
        var stats = new SyncStats();
        int logId = await _db.StartSyncLogAsync("dump");

        _linesRead = 0;
        _apiCalls = 0;
        _completionCounter = 0;
        _inFlight = new();

        try
        {
            Console.WriteLine($"\n  Dump file : {Path.GetFileName(dumpFilePath)}");
            Console.WriteLine($"  Filtri    : popularity >= {_settings.MinPopularity}, runtime >= {_settings.MinRuntimeMinutes} min");
            Console.WriteLine($"  Parallelismo: {_settings.DumpParallelism} worker | max {_settings.DumpMaxRequestsPerSecond} req/s");

            Console.Write("\n  Caricamento ID esistenti... ");
            var existing = await _db.GetExistingMovieTmdbIdsAsync();
            Console.WriteLine($"{existing.Count:N0} già presenti.");

            _dbCheckpoint = await _db.GetDumpProgressAsync();
            _lastCompletedId = _dbCheckpoint;
            if (_dbCheckpoint > 0)
                Console.WriteLine($"  Ripresa dal checkpoint: ID {_dbCheckpoint:N0}");

            Console.WriteLine("\n  Avvio. Ctrl+C per pausare.\n");

            var sw = System.Diagnostics.Stopwatch.StartNew();

            using var rateLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
            {
                TokenLimit = _settings.DumpMaxRequestsPerSecond,
                TokensPerPeriod = _settings.DumpMaxRequestsPerSecond,
                ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = _settings.DumpParallelism * 4,
                AutoReplenishment = true,
            });

            try
            {
                await Parallel.ForEachAsync(
                    ReadFilteredEntriesAsync(dumpFilePath, ct, stats),
                    new ParallelOptions { MaxDegreeOfParallelism = _settings.DumpParallelism, CancellationToken = ct },
                    async (entry, localCt) =>
                    {
                        _inFlight.TryAdd(entry.Id, 0);
                        try
                        {
                            using var lease = await rateLimiter.AcquireAsync(1, localCt);
                            if (!lease.IsAcquired) return;

                            long calls = Interlocked.Increment(ref _apiCalls);

                            TmdbMovieDetailFull? details;
                            try { details = await _tmdb.GetMovieDetailFullParallelAsync(entry.Id); }
                            catch (Exception ex)
                            {
                                Console.Error.WriteLine($"\n  [ERROR] API {entry.Id}: {ex.Message}");
                                lock (stats) stats.Errors++;
                                return;
                            }

                            if (details == null || (details.Runtime > 0 && details.Runtime < _settings.MinRuntimeMinutes))
                            {
                                lock (stats) stats.Skipped++;
                                return;
                            }

                            try
                            {
                                var (movieId, isNew) = await _db.InsertMovieFullAsync(details);
                                await _db.InsertMovieGenresAsync(movieId, details.Genres.Select(g => g.Id).ToList());

                                var certs = ExtractCertifications(details.ReleaseDates);
                                await _db.UpsertCertificationsAsync(movieId, certs.Count > 0 ? certs : [("N/A", "NR")]);

                                if (details.WatchProviders?.Results.Count > 0)
                                {
                                    await _db.UpsertWatchProvidersAsync(movieId, details.WatchProviders);
                                    lock (stats) stats.ProvidersUpdated++;
                                }

                                lock (stats)
                                {
                                    if (isNew) stats.NewMoviesInserted++;
                                    else stats.Enriched++;
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.Error.WriteLine($"\n  [ERROR] insert {entry.Id}: {ex.Message}");
                                lock (stats) stats.Errors++;
                            }

                            if (calls % 25 == 0) PrintProgress(calls, stats, sw);
                        }
                        finally
                        {
                            _inFlight.TryRemove(entry.Id, out _);
                            InterlockedMax(ref _lastCompletedId, entry.Id);
                            await MaybeFlushCheckpointAsync();
                        }
                    }
                );
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }

            if (_lastCompletedId > _dbCheckpoint)
                await _db.UpdateDumpProgressAsync(_lastCompletedId);

            Console.WriteLine();
            Console.WriteLine(ct.IsCancellationRequested
                ? "\n  Pausa. Riprendi con [5] — ripartirà dall'ultimo ID processato."
                : $"\n  Dump completato. Righe lette: {_linesRead:N0} | Chiamate API: {_apiCalls:N0}");
        }
        finally
        {
            await _db.FinishSyncLogAsync(logId, stats,
                ct.IsCancellationRequested ? "Cancelled by user" : null);
        }

        return stats;
    }

    private async Task MaybeFlushCheckpointAsync()
    {
        long count = Interlocked.Increment(ref _completionCounter);
        if (count % _settings.DumpCheckpointEvery != 0) return;

        // Safe to checkpoint everything below the minimum in-flight ID.
        // If nothing is in-flight, use the maximum completed ID.
        int safeId = _inFlight.IsEmpty
            ? Volatile.Read(ref _lastCompletedId)
            : _inFlight.Keys.Min() - 1;

        if (safeId <= Volatile.Read(ref _dbCheckpoint)) return;

        await _checkpointLock.WaitAsync();
        try
        {
            if (safeId > _dbCheckpoint)
            {
                await _db.UpdateDumpProgressAsync(safeId);
                _dbCheckpoint = safeId;
            }
        }
        finally { _checkpointLock.Release(); }
    }

    private async IAsyncEnumerable<TmdbDumpEntry> ReadFilteredEntriesAsync(
        string dumpFilePath,
        [EnumeratorCancellation] CancellationToken ct,
        SyncStats stats)
    {
        await using var fileStream = File.OpenRead(dumpFilePath);
        bool isGzip = dumpFilePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);
        GZipStream? gzip = isGzip ? new GZipStream(fileStream, CompressionMode.Decompress) : null;
        await using var gzipDisposable = gzip;
        Stream dataStream = gzip != null ? (Stream)gzip : fileStream;
        using var reader = new StreamReader(dataStream, leaveOpen: true);

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null && !ct.IsCancellationRequested)
        {
            Interlocked.Increment(ref _linesRead);

            TmdbDumpEntry? entry;
            try { entry = JsonSerializer.Deserialize<TmdbDumpEntry>(line, _json); }
            catch { lock (stats) stats.Errors++; continue; }

            if (entry == null) continue;
            if (entry.Id <= _dbCheckpoint) continue;

            if (entry.Adult || entry.Video || entry.Popularity < _settings.MinPopularity)
            {
                lock (stats) stats.Skipped++;
                continue;
            }

            yield return entry;
        }
    }

    private static void InterlockedMax(ref int location, int value)
    {
        int current;
        do { current = Volatile.Read(ref location); }
        while (current < value && Interlocked.CompareExchange(ref location, value, current) != current);
    }

    private void PrintProgress(long apiCalls, SyncStats stats, System.Diagnostics.Stopwatch sw)
    {
        double rate = apiCalls / Math.Max(sw.Elapsed.TotalSeconds, 1);
        int newM, enriched, skipped, errors;
        lock (stats) { newM = stats.NewMoviesInserted; enriched = stats.Enriched; skipped = stats.Skipped; errors = stats.Errors; }
        Console.Write(
            $"\r  Righe: {Volatile.Read(ref _linesRead):N0} | API: {apiCalls:N0} | " +
            $"Nuovi: {newM:N0} | Arricchiti: {enriched:N0} | " +
            $"Saltati: {skipped:N0} | Err: {errors} | " +
            $"{rate:F1} req/s   ");
    }

    private static List<(string region, string cert)> ExtractCertifications(TmdbReleaseDatesWrapper? wrapper)
    {
        var result = new List<(string, string)>();
        if (wrapper == null) return result;

        foreach (var country in wrapper.Results)
        {
            var cert = country.ReleaseDates
                .Where(rd => rd.Type == 3 && !string.IsNullOrEmpty(rd.Certification))
                .FirstOrDefault()?.Certification
                ?? country.ReleaseDates
                .Where(rd => !string.IsNullOrEmpty(rd.Certification))
                .FirstOrDefault()?.Certification;

            if (cert != null)
                result.Add((country.Region, cert));
        }
        return result;
    }
}
