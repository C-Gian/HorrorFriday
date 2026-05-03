using Npgsql;
using HorrorFriday.TmdbSync.Models;

namespace HorrorFriday.TmdbSync.Services;

/// <summary>
/// All database operations for the TMDB sync.
/// Raw Npgsql only — no ORM, no EF.
/// </summary>
public sealed class SyncDatabaseService : IDisposable
{
    private readonly NpgsqlDataSource _ds;

    public SyncDatabaseService(string connectionString)
    {
        _ds = NpgsqlDataSource.Create(connectionString);
    }

    // ─── Startup checks ──────────────────────────────────────────────────────

    public async Task TestConnectionAsync()
    {
        await using var conn = await _ds.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("SELECT 1", conn);
        await cmd.ExecuteScalarAsync();
    }

    public async Task<bool> MigrationAppliedAsync()
    {
        await using var conn = await _ds.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_name='movies' AND column_name='media_type')",
            conn);
        return (bool)(await cmd.ExecuteScalarAsync() ?? false);
    }

    // ─── Existing content ────────────────────────────────────────────────────

    /// <summary>Returns all (tmdb_id, media_type) pairs already in the database.</summary>
    public async Task<HashSet<(int tmdbId, string mediaType)>> GetExistingItemsAsync()
    {
        var set = new HashSet<(int, string)>();
        await using var conn = await _ds.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("SELECT tmdb_id, media_type FROM movies", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            set.Add((reader.GetInt32(0), reader.GetString(1)));
        return set;
    }

    /// <summary>
    /// Returns a batch of movies/shows that still need certification backfill.
    /// Movies are considered "backfilled" once they have at least one certification row.
    /// On resume after interruption, already-processed items are naturally excluded.
    /// </summary>
    public async Task<List<(int id, int tmdbId, string mediaType)>> GetItemsNeedingBackfillAsync(int limit)
    {
        var list = new List<(int, int, string)>();
        await using var conn = await _ds.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT m.id, m.tmdb_id, m.media_type
            FROM movies m
            LEFT JOIN movie_certifications mc ON mc.movie_id = m.id
            WHERE mc.movie_id IS NULL
            ORDER BY m.id
            LIMIT $1", conn);
        cmd.Parameters.AddWithValue(limit);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2)));
        return list;
    }

    public async Task<int> CountItemsNeedingBackfillAsync()
    {
        await using var conn = await _ds.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT COUNT(*) FROM movies m
            LEFT JOIN movie_certifications mc ON mc.movie_id = m.id
            WHERE mc.movie_id IS NULL", conn);
        return (int)(long)(await cmd.ExecuteScalarAsync() ?? 0L);
    }

    // ─── Insert new movies ───────────────────────────────────────────────────

    /// <summary>
    /// Lightweight insert using only the fields available in a discover-page result.
    /// Used by fast-discover mode (Mode 4) — no extra API call per record.
    /// Certifications, providers, runtime, etc. are filled later by Mode 3 backfill.
    /// </summary>
    public async Task<int?> InsertMovieFromDiscoverAsync(TmdbMovieResult m)
    {
        DateOnly? releaseDate = TryParseDate(m.ReleaseDate);
        short? releaseYear = (short?)releaseDate?.Year;

        await using var conn = await _ds.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO movies
                (tmdb_id, title, original_title, overview, release_year, release_date,
                 vote_average, vote_count, popularity, original_language, is_adult,
                 poster_path, media_type)
            VALUES
                ($1, $2, $3, $4, $5, $6,
                 $7, $8, $9, $10, $11,
                 $12, 'movie')
            ON CONFLICT (tmdb_id, media_type) DO NOTHING
            RETURNING id", conn);

        cmd.Parameters.AddWithValue(m.Id);
        cmd.Parameters.AddWithValue(m.Title ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(m.OriginalTitle ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(m.Overview ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(releaseYear.HasValue ? (object)releaseYear.Value : DBNull.Value);
        cmd.Parameters.AddWithValue(releaseDate.HasValue ? (object)releaseDate.Value : DBNull.Value);
        cmd.Parameters.AddWithValue((decimal)m.VoteAverage);
        cmd.Parameters.AddWithValue(m.VoteCount);
        cmd.Parameters.AddWithValue((decimal)m.Popularity);
        cmd.Parameters.AddWithValue(m.OriginalLanguage ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(m.Adult);
        cmd.Parameters.AddWithValue(m.PosterPath ?? (object)DBNull.Value);

        var result = await cmd.ExecuteScalarAsync();
        return result is int id ? id : null;
    }

    /// <summary>
    /// Lightweight insert for TV shows using only discover-page fields.
    /// </summary>
    public async Task<int?> InsertTvFromDiscoverAsync(TmdbTvResult tv)
    {
        DateOnly? airDate = TryParseDate(tv.FirstAirDate);
        short? releaseYear = (short?)airDate?.Year;

        await using var conn = await _ds.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO movies
                (tmdb_id, title, original_title, overview, release_year, release_date,
                 vote_average, vote_count, popularity, original_language, is_adult,
                 poster_path, media_type)
            VALUES
                ($1, $2, $3, $4, $5, $6,
                 $7, $8, $9, $10, false,
                 $11, 'tv')
            ON CONFLICT (tmdb_id, media_type) DO NOTHING
            RETURNING id", conn);

        cmd.Parameters.AddWithValue(tv.Id);
        cmd.Parameters.AddWithValue(tv.Name ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(tv.OriginalName ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(tv.Overview ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(releaseYear.HasValue ? (object)releaseYear.Value : DBNull.Value);
        cmd.Parameters.AddWithValue(airDate.HasValue ? (object)airDate.Value : DBNull.Value);
        cmd.Parameters.AddWithValue((decimal)tv.VoteAverage);
        cmd.Parameters.AddWithValue(tv.VoteCount);
        cmd.Parameters.AddWithValue((decimal)tv.Popularity);
        cmd.Parameters.AddWithValue(tv.OriginalLanguage ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(tv.PosterPath ?? (object)DBNull.Value);

        var result = await cmd.ExecuteScalarAsync();
        return result is int id ? id : null;
    }

    /// <summary>
    /// Inserts a new movie from TMDB detail data.
    /// embedding is intentionally NULL — no OpenAI calls here.
    /// Returns the new database id, or null if it already existed (ON CONFLICT).
    /// </summary>
    public async Task<int?> InsertMovieAsync(TmdbMovieDetails m)
    {
        DateOnly? releaseDate = TryParseDate(m.ReleaseDate);
        short? releaseYear = (short?)releaseDate?.Year;

        await using var conn = await _ds.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO movies
                (tmdb_id, title, original_title, overview, release_year, release_date,
                 runtime_minutes, vote_average, vote_count, popularity, status,
                 original_language, is_adult, tagline, poster_path, imdb_id, media_type)
            VALUES
                ($1, $2, $3, $4, $5, $6,
                 $7, $8, $9, $10, $11,
                 $12, $13, $14, $15, $16, 'movie')
            ON CONFLICT (tmdb_id, media_type) DO NOTHING
            RETURNING id", conn);

        cmd.Parameters.AddWithValue(m.Id);
        cmd.Parameters.AddWithValue(m.Title ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(m.OriginalTitle ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(m.Overview ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(releaseYear.HasValue ? (object)releaseYear.Value : DBNull.Value);
        cmd.Parameters.AddWithValue(releaseDate.HasValue ? (object)releaseDate.Value : DBNull.Value);
        cmd.Parameters.AddWithValue(m.Runtime > 0 ? (object)(short)m.Runtime : DBNull.Value);
        cmd.Parameters.AddWithValue((decimal)m.VoteAverage);
        cmd.Parameters.AddWithValue(m.VoteCount);
        cmd.Parameters.AddWithValue((decimal)m.Popularity);
        cmd.Parameters.AddWithValue(m.Status ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(m.OriginalLanguage ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(m.Adult);
        cmd.Parameters.AddWithValue(m.Tagline ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(m.PosterPath ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(m.ImdbId ?? (object)DBNull.Value);

        var result = await cmd.ExecuteScalarAsync();
        return result is int id ? id : null;
    }

    /// <summary>
    /// Inserts a new TV show. Maps TV-specific fields to the shared movies table.
    /// Returns the new database id, or null if already existed.
    /// </summary>
    public async Task<int?> InsertTvShowAsync(TmdbTvDetails tv)
    {
        DateOnly? airDate = TryParseDate(tv.FirstAirDate);
        short? releaseYear = (short?)airDate?.Year;
        short? runtime = tv.EpisodeRunTime?.FirstOrDefault() is int r and > 0 ? (short)r : null;

        await using var conn = await _ds.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO movies
                (tmdb_id, title, original_title, overview, release_year, release_date,
                 runtime_minutes, vote_average, vote_count, popularity, status,
                 original_language, is_adult, poster_path, media_type)
            VALUES
                ($1, $2, $3, $4, $5, $6,
                 $7, $8, $9, $10, $11,
                 $12, false, $13, 'tv')
            ON CONFLICT (tmdb_id, media_type) DO NOTHING
            RETURNING id", conn);

        cmd.Parameters.AddWithValue(tv.Id);
        cmd.Parameters.AddWithValue(tv.Name ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(tv.OriginalName ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(tv.Overview ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(releaseYear.HasValue ? (object)releaseYear.Value : DBNull.Value);
        cmd.Parameters.AddWithValue(airDate.HasValue ? (object)airDate.Value : DBNull.Value);
        cmd.Parameters.AddWithValue(runtime.HasValue ? (object)runtime.Value : DBNull.Value);
        cmd.Parameters.AddWithValue((decimal)tv.VoteAverage);
        cmd.Parameters.AddWithValue(tv.VoteCount);
        cmd.Parameters.AddWithValue((decimal)tv.Popularity);
        cmd.Parameters.AddWithValue(tv.Status ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(tv.OriginalLanguage ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(tv.PosterPath ?? (object)DBNull.Value);

        var result = await cmd.ExecuteScalarAsync();
        return result is int id ? id : null;
    }

    // ─── Genres ──────────────────────────────────────────────────────────────

    /// <summary>Links a movie to genres that already exist in our genres table.</summary>
    public async Task InsertMovieGenresAsync(int movieId, IEnumerable<int> genreIds)
    {
        var ids = genreIds.ToArray();
        if (ids.Length == 0) return;

        await using var conn = await _ds.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO movie_genres (movie_id, genre_id)
            SELECT $1, g.id
            FROM unnest($2::integer[]) AS g(id)
            JOIN genres ON genres.id = g.id
            ON CONFLICT DO NOTHING", conn);
        cmd.Parameters.AddWithValue(movieId);
        cmd.Parameters.AddWithValue(ids);
        await cmd.ExecuteNonQueryAsync();
    }

    // ─── Certifications ──────────────────────────────────────────────────────

    /// <summary>Inserts or updates certifications for the given movie/show.</summary>
    public async Task UpsertCertificationsAsync(int movieId, IEnumerable<(string region, string cert)> certs)
    {
        var list = certs.Where(c => !string.IsNullOrEmpty(c.cert)).ToList();
        if (list.Count == 0) return;

        await using var conn = await _ds.OpenConnectionAsync();
        foreach (var (region, cert) in list)
        {
            await using var cmd = new NpgsqlCommand(@"
                INSERT INTO movie_certifications (movie_id, region, certification)
                VALUES ($1, $2, $3)
                ON CONFLICT (movie_id, region) DO UPDATE SET certification = EXCLUDED.certification",
                conn);
            cmd.Parameters.AddWithValue(movieId);
            cmd.Parameters.AddWithValue(region);
            cmd.Parameters.AddWithValue(cert);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ─── Watch providers ─────────────────────────────────────────────────────

    /// <summary>
    /// Inserts or refreshes watch-provider links for the given movie/show.
    /// First ensures all provider records exist in the watch_providers catalogue,
    /// then writes the movie_watch_providers mapping rows.
    /// </summary>
    public async Task UpsertWatchProvidersAsync(int movieId, TmdbWatchProvidersWrapper? wrapper)
    {
        if (wrapper?.Results is null || wrapper.Results.Count == 0) return;

        await using var conn = await _ds.OpenConnectionAsync();

        foreach (var (region, regionData) in wrapper.Results)
        {
            var allProviders = new List<(TmdbProvider provider, string type)>();
            if (regionData.Stream != null)
                allProviders.AddRange(regionData.Stream.Select(p => (p, "stream")));
            if (regionData.Rent != null)
                allProviders.AddRange(regionData.Rent.Select(p => (p, "rent")));
            if (regionData.Buy != null)
                allProviders.AddRange(regionData.Buy.Select(p => (p, "buy")));
            if (regionData.Ads != null)
                allProviders.AddRange(regionData.Ads.Select(p => (p, "ads")));

            foreach (var (provider, type) in allProviders)
            {
                // Ensure provider exists in catalogue
                await using var upsertProvider = new NpgsqlCommand(@"
                    INSERT INTO watch_providers (tmdb_provider_id, provider_name, logo_path)
                    VALUES ($1, $2, $3)
                    ON CONFLICT (tmdb_provider_id) DO UPDATE
                        SET provider_name = EXCLUDED.provider_name,
                            logo_path     = EXCLUDED.logo_path
                    RETURNING id", conn);
                upsertProvider.Parameters.AddWithValue(provider.ProviderId);
                upsertProvider.Parameters.AddWithValue(provider.ProviderName);
                upsertProvider.Parameters.AddWithValue(provider.LogoPath ?? (object)DBNull.Value);
                var providerId = (int)(await upsertProvider.ExecuteScalarAsync())!;

                // Link movie → provider
                await using var upsertLink = new NpgsqlCommand(@"
                    INSERT INTO movie_watch_providers (movie_id, provider_id, region, provider_type)
                    VALUES ($1, $2, $3, $4)
                    ON CONFLICT DO NOTHING", conn);
                upsertLink.Parameters.AddWithValue(movieId);
                upsertLink.Parameters.AddWithValue(providerId);
                upsertLink.Parameters.AddWithValue(region);
                upsertLink.Parameters.AddWithValue(type);
                await upsertLink.ExecuteNonQueryAsync();
            }
        }
    }

    // ─── Refresh existing movie stats ────────────────────────────────────────

    /// <summary>
    /// Updates vote_average, vote_count, popularity, and poster_path for an
    /// existing movie during backfill — these fields change over time.
    /// </summary>
    public async Task UpdateMovieStatsAsync(int movieId, double voteAverage, int voteCount,
        double popularity, string? posterPath)
    {
        await using var conn = await _ds.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            UPDATE movies SET
                vote_average = $2,
                vote_count   = $3,
                popularity   = $4,
                poster_path  = COALESCE($5, poster_path)
            WHERE id = $1", conn);
        cmd.Parameters.AddWithValue(movieId);
        cmd.Parameters.AddWithValue((decimal)voteAverage);
        cmd.Parameters.AddWithValue(voteCount);
        cmd.Parameters.AddWithValue((decimal)popularity);
        cmd.Parameters.AddWithValue(posterPath ?? (object)DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    // ─── Sync log ────────────────────────────────────────────────────────────

    public async Task<int> StartSyncLogAsync(string syncType)
    {
        await using var conn = await _ds.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO tmdb_sync_log (sync_type) VALUES ($1) RETURNING id", conn);
        cmd.Parameters.AddWithValue(syncType);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task FinishSyncLogAsync(int logId, SyncStats stats, string? error = null)
    {
        await using var conn = await _ds.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            UPDATE tmdb_sync_log SET
                finished_at            = NOW(),
                new_movies_inserted    = $2,
                new_tv_inserted        = $3,
                certifications_updated = $4,
                providers_updated      = $5,
                errors                 = $6,
                notes                  = $7
            WHERE id = $1", conn);
        cmd.Parameters.AddWithValue(logId);
        cmd.Parameters.AddWithValue(stats.NewMoviesInserted);
        cmd.Parameters.AddWithValue(stats.NewTvInserted);
        cmd.Parameters.AddWithValue(stats.CertificationsUpdated);
        cmd.Parameters.AddWithValue(stats.ProvidersUpdated);
        cmd.Parameters.AddWithValue(stats.Errors);
        cmd.Parameters.AddWithValue(error ?? (object)DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static DateOnly? TryParseDate(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        return DateOnly.TryParse(s, out var d) ? d : null;
    }

    public void Dispose() => _ds.Dispose();
}
