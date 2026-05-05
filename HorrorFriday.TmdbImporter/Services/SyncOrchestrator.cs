using HorrorFriday.TmdbImporter.Models;

namespace HorrorFriday.TmdbImporter.Services;

/// <summary>
/// Coordinates TMDB discovery and database sync across all four run modes.
/// All public methods write progress to stdout and return a SyncStats summary.
/// </summary>
public sealed class SyncOrchestrator
{
    private readonly TmdbApiService _tmdb;
    private readonly SyncDatabaseService _db;
    private readonly SyncSettings _settings;

    public SyncOrchestrator(TmdbApiService tmdb, SyncDatabaseService db, SyncSettings settings)
    {
        _tmdb = tmdb;
        _db = db;
        _settings = settings;
    }

    // ─── Mode 1: Full Sync ───────────────────────────────────────────────────

    /// <summary>
    /// Discovers ALL horror movies and TV shows on TMDB (paginating year by year
    /// to stay within the 10 000-item discover limit) and inserts anything not
    /// already in the database. Then runs a full metadata backfill.
    /// Expected to take many hours on the first run.
    /// </summary>
    public async Task<SyncStats> RunFullSyncAsync(CancellationToken ct)
    {
        var stats = new SyncStats();
        int logId = await _db.StartSyncLogAsync("full");

        try
        {
            Print("\n[1/3] Loading existing items from database...");
            var existing = await _db.GetExistingItemsAsync();
            PrintLine($" {existing.Count:N0} items already in DB.");

            Print("[2/3] Discovering all movies on TMDB (year by year)...\n");
            await DiscoverAllMoviesAsync(existing, stats, ct, fastMode: false);

            Print("\n[2/3] Discovering all TV shows on TMDB (year by year)...\n");
            await DiscoverAllTvAsync(existing, stats, ct, fastMode: false);

            if (!ct.IsCancellationRequested)
            {
                PrintLine($"\n[3/3] Starting metadata backfill for ALL items in database...");
                await RunBackfillCoreAsync(stats, ct);
            }
        }
        finally
        {
            await _db.FinishSyncLogAsync(logId, stats,
                ct.IsCancellationRequested ? "Cancelled by user" : null);
        }

        return stats;
    }

    // ─── Mode 2: Daily Delta ─────────────────────────────────────────────────

    /// <summary>
    /// Imports only movies and TV shows released or updated in the last N days.
    /// Each new item gets its certifications and providers fetched immediately.
    /// Fast enough for daily runs.
    /// </summary>
    public async Task<SyncStats> RunDailySyncAsync(CancellationToken ct)
    {
        var stats = new SyncStats();
        int logId = await _db.StartSyncLogAsync("daily");

        try
        {
            var since = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-_settings.DailyLookbackDays));
            PrintLine($"\nLooking back {_settings.DailyLookbackDays} days (since {since:yyyy-MM-dd}).");

            Print("[1/2] Loading existing items from database...");
            var existing = await _db.GetExistingItemsAsync();
            PrintLine($" {existing.Count:N0} items in DB.");

            Print("[2/2] Discovering new movies...\n");
            await DiscoverMoviesSinceAsync(existing, stats, since, ct, fastMode: false);

            if (!ct.IsCancellationRequested)
            {
                Print("      Discovering new TV shows...\n");
                await DiscoverTvSinceAsync(existing, stats, since, ct, fastMode: false);
            }
        }
        finally
        {
            await _db.FinishSyncLogAsync(logId, stats,
                ct.IsCancellationRequested ? "Cancelled by user" : null);
        }

        return stats;
    }

    // ─── Mode 3: Backfill Only ───────────────────────────────────────────────

    /// <summary>
    /// Fetches certifications and watch providers for every item in the database
    /// that does not yet have a certification entry. Self-resuming: restart the
    /// tool anytime and it picks up exactly where it left off.
    /// </summary>
    public async Task<SyncStats> RunBackfillOnlyAsync(CancellationToken ct)
    {
        var stats = new SyncStats();
        int logId = await _db.StartSyncLogAsync("backfill");

        try
        {
            await RunBackfillCoreAsync(stats, ct);
        }
        finally
        {
            await _db.FinishSyncLogAsync(logId, stats,
                ct.IsCancellationRequested ? "Cancelled by user" : null);
        }

        return stats;
    }

    // ─── Mode 4: Discover Only ───────────────────────────────────────────────

    /// <summary>
    /// Discovers and inserts new movies/shows without running the backfill.
    /// Pass null for lookbackDays to discover everything (all years).
    /// </summary>
    public async Task<SyncStats> RunDiscoverOnlyAsync(int? lookbackDays, CancellationToken ct)
    {
        var stats = new SyncStats();
        int logId = await _db.StartSyncLogAsync("discover");

        try
        {
            Print("\nLoading existing items from database...");
            var existing = await _db.GetExistingItemsAsync();
            PrintLine($" {existing.Count:N0} items in DB.");

            if (lookbackDays.HasValue)
            {
                var since = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-lookbackDays.Value));
                PrintLine($"Discovering items released after {since:yyyy-MM-dd}.\n");
                await DiscoverMoviesSinceAsync(existing, stats, since, ct, fastMode: true);
                if (!ct.IsCancellationRequested)
                    await DiscoverTvSinceAsync(existing, stats, since, ct, fastMode: true);
            }
            else
            {
                PrintLine("Discovering ALL movies/shows (full year sweep) — fast mode, no detail API call per record.\n");
                await DiscoverAllMoviesAsync(existing, stats, ct, fastMode: true);
                if (!ct.IsCancellationRequested)
                    await DiscoverAllTvAsync(existing, stats, ct, fastMode: true);
            }
        }
        finally
        {
            await _db.FinishSyncLogAsync(logId, stats,
                ct.IsCancellationRequested ? "Cancelled by user" : null);
        }

        return stats;
    }

    // ─── Discovery helpers ───────────────────────────────────────────────────

    private async Task DiscoverAllMoviesAsync(
        HashSet<(int, string)> existing, SyncStats stats, CancellationToken ct, bool fastMode)
    {
        string genreParam = string.Join(",", _settings.HorrorGenreIds);
        int currentYear = DateTime.UtcNow.Year;

        for (int year = currentYear; year >= 1900 && !ct.IsCancellationRequested; year--)
        {
            int page = 1, totalPages = 1;
            do
            {
                if (ct.IsCancellationRequested) break;
                var result = await _tmdb.DiscoverMoviesAsync(genreParam, page, year: year);
                if (result == null) break;
                totalPages = Math.Min(result.TotalPages, 500);

                foreach (var item in result.Results)
                {
                    if (ct.IsCancellationRequested) break;
                    if (existing.Contains((item.Id, "movie"))) continue;

                    if (fastMode)
                        await InsertDiscoveredMovieAsync(item, stats);
                    else
                        await InsertNewMovieAsync(item.Id, item.GenreIds, stats);

                    existing.Add((item.Id, "movie"));
                }

                Console.Write($"\r  Movies {year}: page {page}/{totalPages} | +{stats.NewMoviesInserted} new | {stats.Errors} err   ");
                page++;
            } while (page <= totalPages);
        }
        Console.WriteLine();
    }

    private async Task DiscoverAllTvAsync(
        HashSet<(int, string)> existing, SyncStats stats, CancellationToken ct, bool fastMode)
    {
        string genreParam = string.Join(",", _settings.HorrorGenreIds);
        int currentYear = DateTime.UtcNow.Year;

        for (int year = currentYear; year >= 1950 && !ct.IsCancellationRequested; year--)
        {
            int page = 1, totalPages = 1;
            do
            {
                if (ct.IsCancellationRequested) break;
                var result = await _tmdb.DiscoverTvAsync(genreParam, page, year: year);
                if (result == null) break;
                totalPages = Math.Min(result.TotalPages, 500);

                foreach (var item in result.Results)
                {
                    if (ct.IsCancellationRequested) break;
                    if (existing.Contains((item.Id, "tv"))) continue;

                    if (fastMode)
                        await InsertDiscoveredTvAsync(item, stats);
                    else
                        await InsertNewTvShowAsync(item.Id, item.GenreIds, stats);

                    existing.Add((item.Id, "tv"));
                }

                Console.Write($"\r  TV {year}: page {page}/{totalPages} | +{stats.NewTvInserted} new | {stats.Errors} err   ");
                page++;
            } while (page <= totalPages);
        }
        Console.WriteLine();
    }

    private async Task DiscoverMoviesSinceAsync(
        HashSet<(int, string)> existing, SyncStats stats, DateOnly since, CancellationToken ct, bool fastMode)
    {
        string genreParam = string.Join(",", _settings.HorrorGenreIds);
        int page = 1, totalPages = 1;
        do
        {
            if (ct.IsCancellationRequested) break;
            var result = await _tmdb.DiscoverMoviesAsync(genreParam, page, releasedAfter: since);
            if (result == null) break;
            totalPages = Math.Min(result.TotalPages, 500);

            foreach (var item in result.Results)
            {
                if (ct.IsCancellationRequested) break;
                if (existing.Contains((item.Id, "movie"))) continue;

                if (fastMode)
                    await InsertDiscoveredMovieAsync(item, stats);
                else
                    await InsertNewMovieAsync(item.Id, item.GenreIds, stats);

                existing.Add((item.Id, "movie"));
            }

            Console.Write($"\r  Movies: page {page}/{totalPages} | +{stats.NewMoviesInserted} new | {stats.Errors} err   ");
            page++;
        } while (page <= totalPages);
        Console.WriteLine();
    }

    private async Task DiscoverTvSinceAsync(
        HashSet<(int, string)> existing, SyncStats stats, DateOnly since, CancellationToken ct, bool fastMode)
    {
        string genreParam = string.Join(",", _settings.HorrorGenreIds);
        int page = 1, totalPages = 1;
        do
        {
            if (ct.IsCancellationRequested) break;
            var result = await _tmdb.DiscoverTvAsync(genreParam, page, airedAfter: since);
            if (result == null) break;
            totalPages = Math.Min(result.TotalPages, 500);

            foreach (var item in result.Results)
            {
                if (ct.IsCancellationRequested) break;
                if (existing.Contains((item.Id, "tv"))) continue;

                if (fastMode)
                    await InsertDiscoveredTvAsync(item, stats);
                else
                    await InsertNewTvShowAsync(item.Id, item.GenreIds, stats);

                existing.Add((item.Id, "tv"));
            }

            Console.Write($"\r  TV shows: page {page}/{totalPages} | +{stats.NewTvInserted} new | {stats.Errors} err   ");
            page++;
        } while (page <= totalPages);
        Console.WriteLine();
    }

    // Fast-mode helpers: insert from discover page data, no extra API call
    private async Task InsertDiscoveredMovieAsync(TmdbMovieResult item, SyncStats stats)
    {
        try
        {
            var movieId = await _db.InsertMovieFromDiscoverAsync(item);
            if (movieId.HasValue)
            {
                await _db.InsertMovieGenresAsync(movieId.Value, item.GenreIds);
                stats.NewMoviesInserted++;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\n  [ERROR] movie {item.Id}: {ex.Message}");
            stats.Errors++;
        }
    }

    private async Task InsertDiscoveredTvAsync(TmdbTvResult item, SyncStats stats)
    {
        try
        {
            var tvId = await _db.InsertTvFromDiscoverAsync(item);
            if (tvId.HasValue)
            {
                await _db.InsertMovieGenresAsync(tvId.Value, item.GenreIds);
                stats.NewTvInserted++;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\n  [ERROR] tv {item.Id}: {ex.Message}");
            stats.Errors++;
        }
    }

    // ─── Insert single new item ───────────────────────────────────────────────

    private async Task InsertNewMovieAsync(int tmdbId, List<int> genreIds, SyncStats stats)
    {
        try
        {
            var details = await _tmdb.GetMovieDetailsAsync(tmdbId);
            if (details == null) { stats.Errors++; return; }

            var movieId = await _db.InsertMovieAsync(details);
            if (movieId == null) return; // already existed (race condition edge case)

            await _db.InsertMovieGenresAsync(movieId.Value, genreIds);
            await InsertMetadataAsync(movieId.Value, "movie", details.ReleaseDates, null, details.WatchProviders, stats);

            stats.NewMoviesInserted++;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\n  [ERROR] movie {tmdbId}: {ex.Message}");
            stats.Errors++;
        }
    }

    private async Task InsertNewTvShowAsync(int tmdbId, List<int> genreIds, SyncStats stats)
    {
        try
        {
            var details = await _tmdb.GetTvDetailsAsync(tmdbId);
            if (details == null) { stats.Errors++; return; }

            var movieId = await _db.InsertTvShowAsync(details);
            if (movieId == null) return;

            await _db.InsertMovieGenresAsync(movieId.Value, genreIds);
            await InsertMetadataAsync(movieId.Value, "tv", null, details.ContentRatings, details.WatchProviders, stats);

            stats.NewTvInserted++;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\n  [ERROR] tv {tmdbId}: {ex.Message}");
            stats.Errors++;
        }
    }

    // ─── Backfill core ───────────────────────────────────────────────────────

    private async Task RunBackfillCoreAsync(SyncStats stats, CancellationToken ct)
    {
        int total = await _db.CountItemsNeedingBackfillAsync();
        PrintLine($"Items needing metadata backfill: {total:N0}");

        if (total == 0)
        {
            PrintLine("Nothing to backfill — all items already have certification data.");
            return;
        }

        PrintLine("Processing in batches. Press Ctrl+C anytime to pause; restart to resume.\n");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        int processed = 0;

        while (!ct.IsCancellationRequested)
        {
            var batch = await _db.GetItemsNeedingBackfillAsync(_settings.BackfillBatchSize);
            if (batch.Count == 0) break;

            foreach (var (dbId, tmdbId, mediaType) in batch)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    if (mediaType == "tv")
                    {
                        var details = await _tmdb.GetTvDetailsAsync(tmdbId);
                        if (details != null)
                        {
                            await _db.UpdateMovieStatsAsync(dbId, details.VoteAverage,
                                details.VoteCount, details.Popularity, details.PosterPath);
                            await InsertMetadataAsync(dbId, "tv", null,
                                details.ContentRatings, details.WatchProviders, stats);
                        }
                        else
                        {
                            // TMDB returned 404 — mark as processed by inserting empty cert row
                            await _db.UpsertCertificationsAsync(dbId, [("N/A", "NR")]);
                            stats.Errors++;
                        }
                    }
                    else
                    {
                        var details = await _tmdb.GetMovieDetailsAsync(tmdbId);
                        if (details != null)
                        {
                            await _db.UpdateMovieStatsAsync(dbId, details.VoteAverage,
                                details.VoteCount, details.Popularity, details.PosterPath);
                            await InsertMetadataAsync(dbId, "movie",
                                details.ReleaseDates, null, details.WatchProviders, stats);
                        }
                        else
                        {
                            await _db.UpsertCertificationsAsync(dbId, [("N/A", "NR")]);
                            stats.Errors++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"\n  [ERROR] {mediaType} {tmdbId}: {ex.Message}");
                    // Mark as processed so we don't get stuck on a broken record
                    await _db.UpsertCertificationsAsync(dbId, [("N/A", "NR")]);
                    stats.Errors++;
                }

                processed++;
                PrintBackfillProgress(processed, total, sw, stats);
            }
        }

        Console.WriteLine();
        PrintLine(ct.IsCancellationRequested
            ? $"Paused at {processed:N0} / {total:N0}. Restart to continue from this point."
            : $"Backfill complete. {processed:N0} items processed.");
    }

    // ─── Metadata helpers ────────────────────────────────────────────────────

    private async Task InsertMetadataAsync(
        int dbId, string mediaType,
        TmdbReleaseDatesWrapper? movieReleaseDates,
        TmdbContentRatingsWrapper? tvContentRatings,
        TmdbWatchProvidersWrapper? watchProviders,
        SyncStats stats)
    {
        var certs = ExtractCertifications(mediaType, movieReleaseDates, tvContentRatings);
        if (certs.Count > 0)
        {
            await _db.UpsertCertificationsAsync(dbId, certs);
            stats.CertificationsUpdated++;
        }
        else
        {
            // Insert a sentinel so the item is not endlessly re-queued during backfill
            await _db.UpsertCertificationsAsync(dbId, [("N/A", "NR")]);
        }

        if (watchProviders?.Results.Count > 0)
        {
            await _db.UpsertWatchProvidersAsync(dbId, watchProviders);
            stats.ProvidersUpdated++;
        }
    }

    private List<(string region, string cert)> ExtractCertifications(
        string mediaType,
        TmdbReleaseDatesWrapper? movieDates,
        TmdbContentRatingsWrapper? tvRatings)
    {
        var result = new List<(string, string)>();

        if (mediaType == "tv" && tvRatings != null)
        {
            foreach (var r in tvRatings.Results)
                if (!string.IsNullOrEmpty(r.Rating))
                    result.Add((r.Region, r.Rating));
        }
        else if (movieDates != null)
        {
            foreach (var country in movieDates.Results)
            {
                // Prefer theatrical release (type=3), fall back to any non-empty cert
                var cert = country.ReleaseDates
                    .Where(rd => rd.Type == 3 && !string.IsNullOrEmpty(rd.Certification))
                    .FirstOrDefault()?.Certification
                    ?? country.ReleaseDates
                    .Where(rd => !string.IsNullOrEmpty(rd.Certification))
                    .FirstOrDefault()?.Certification;

                if (cert != null)
                    result.Add((country.Region, cert));
            }
        }

        return result;
    }

    // ─── Progress display ────────────────────────────────────────────────────

    private static void PrintBackfillProgress(
        int processed, int total,
        System.Diagnostics.Stopwatch sw, SyncStats stats)
    {
        if (processed % 50 != 0) return;

        double pct = (double)processed / total * 100;
        double rate = processed / Math.Max(sw.Elapsed.TotalSeconds, 1);
        double remaining = (total - processed) / Math.Max(rate, 0.01);
        var eta = TimeSpan.FromSeconds(remaining);

        Console.Write(
            $"\r  [{processed:N0}/{total:N0}] {pct:F1}% | " +
            $"{rate:F1} req/s | ETA {eta:hh\\:mm\\:ss} | " +
            $"certs:{stats.CertificationsUpdated:N0} providers:{stats.ProvidersUpdated:N0} err:{stats.Errors}   ");
    }

    private static void Print(string msg) => Console.Write(msg);
    private static void PrintLine(string msg) => Console.WriteLine(msg);
}
