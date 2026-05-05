using System.Collections.Concurrent;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.RateLimiting;
using HorrorFriday.TmdbImporter.Models;

namespace HorrorFriday.TmdbImporter.Services;

/// <summary>
/// Mode [6]: imports from the official TMDB daily TV series dump file.
///
/// Flow identical to Mode [5] (movie dump) — see TmdbDumpImportService.
/// Key differences:
///   - Source: tv_series_ids_MM_DD_YYYY.json.gz
///   - No video/runtime pre-filter (episode runtime varies)
///   - Certifications from content_ratings instead of release_dates
///   - Creator from created_by instead of crew Director
///   - Checkpoint stored in tmdb_tv_dump_progress table
/// </summary>
public sealed class TmdbTvDumpImportService
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

    public TmdbTvDumpImportService(TmdbApiService tmdb, SyncDatabaseService db, SyncSettings settings)
    {
        _tmdb = tmdb;
        _db = db;
        _settings = settings;
    }

    public async Task<SyncStats> RunAsync(string dumpFilePath, CancellationToken ct)
    {
        var stats = new SyncStats();
        int logId = await _db.StartSyncLogAsync("tv-dump");

        _linesRead = 0;
        _apiCalls = 0;
        _completionCounter = 0;
        _inFlight = new();

        try
        {
            Console.WriteLine($"\n  Dump file : {Path.GetFileName(dumpFilePath)}");
            Console.WriteLine($"  Filtri    : popularity >= {_settings.MinPopularity}");
            Console.WriteLine($"  Parallelismo: {_settings.DumpParallelism} worker | max {_settings.DumpMaxRequestsPerSecond} req/s");

            Console.Write("\n  Caricamento ID esistenti... ");
            var existing = await _db.GetExistingTvTmdbIdsAsync();
            Console.WriteLine($"{existing.Count:N0} già presenti.");

            _dbCheckpoint = await _db.GetTvDumpProgressAsync();
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

                            TmdbTvDetailFull? details;
                            try { details = await _tmdb.GetTvDetailFullParallelAsync(entry.Id); }
                            catch (Exception ex)
                            {
                                Console.Error.WriteLine($"\n  [ERROR] API {entry.Id}: {ex.Message}");
                                lock (stats) stats.Errors++;
                                return;
                            }

                            if (details == null)
                            {
                                lock (stats) stats.Skipped++;
                                return;
                            }

                            try
                            {
                                var (movieId, isNew) = await _db.InsertTvFullAsync(details);
                                await _db.InsertMovieGenresAsync(movieId, details.Genres.Select(g => g.Id).ToList());

                                var certs = ExtractTvCertifications(details.ContentRatings);
                                await _db.UpsertCertificationsAsync(movieId, certs.Count > 0 ? certs : [("N/A", "NR")]);

                                if (details.WatchProviders?.Results.Count > 0)
                                {
                                    await _db.UpsertWatchProvidersAsync(movieId, details.WatchProviders);
                                    lock (stats) stats.ProvidersUpdated++;
                                }

                                lock (stats)
                                {
                                    if (isNew) stats.NewTvInserted++;
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
                await _db.UpdateTvDumpProgressAsync(_lastCompletedId);

            Console.WriteLine();
            Console.WriteLine(ct.IsCancellationRequested
                ? "\n  Pausa. Riprendi con [6] — ripartirà dall'ultimo ID processato."
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

        int safeId = _inFlight.IsEmpty
            ? Volatile.Read(ref _lastCompletedId)
            : _inFlight.Keys.Min() - 1;

        if (safeId <= Volatile.Read(ref _dbCheckpoint)) return;

        await _checkpointLock.WaitAsync();
        try
        {
            if (safeId > _dbCheckpoint)
            {
                await _db.UpdateTvDumpProgressAsync(safeId);
                _dbCheckpoint = safeId;
            }
        }
        finally { _checkpointLock.Release(); }
    }

    private async IAsyncEnumerable<TmdbTvDumpEntry> ReadFilteredEntriesAsync(
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

            TmdbTvDumpEntry? entry;
            try { entry = JsonSerializer.Deserialize<TmdbTvDumpEntry>(line, _json); }
            catch { lock (stats) stats.Errors++; continue; }

            if (entry == null) continue;
            if (entry.Id <= _dbCheckpoint) continue;

            if (entry.Adult || entry.Popularity < _settings.MinPopularity)
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
        int newTv, enriched, skipped, errors;
        lock (stats) { newTv = stats.NewTvInserted; enriched = stats.Enriched; skipped = stats.Skipped; errors = stats.Errors; }
        Console.Write(
            $"\r  Righe: {Volatile.Read(ref _linesRead):N0} | API: {apiCalls:N0} | " +
            $"Nuove: {newTv:N0} | Arricchite: {enriched:N0} | " +
            $"Saltate: {skipped:N0} | Err: {errors} | " +
            $"{rate:F1} req/s   ");
    }

    private static List<(string region, string cert)> ExtractTvCertifications(TmdbContentRatingsWrapper? wrapper)
    {
        if (wrapper == null) return [];
        return wrapper.Results
            .Where(r => !string.IsNullOrEmpty(r.Rating))
            .Select(r => (r.Region, r.Rating))
            .ToList();
    }
}
