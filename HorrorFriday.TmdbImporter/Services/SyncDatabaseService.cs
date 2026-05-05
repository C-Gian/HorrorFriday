using Npgsql;
using HorrorFriday.TmdbImporter.Models;

namespace HorrorFriday.TmdbImporter.Services;

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

    // ─── Dump import ─────────────────────────────────────────────────────────

    /// <summary>All movie tmdb_ids already in the DB — used to skip known records during dump import.</summary>
    public async Task<HashSet<int>> GetExistingMovieTmdbIdsAsync()
    {
        var set = new HashSet<int>();
        await using var conn = await _ds.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT tmdb_id FROM movies WHERE media_type = 'movie'", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            set.Add(reader.GetInt32(0));
        return set;
    }

    /// <summary>
    /// Full upsert from a dump-import detail response.
    /// New records are inserted with all fields; existing records get the detail
    /// fields filled in via COALESCE (never overwrites non-null values).
    /// Returns (id, isNew): isNew=true when the row was freshly inserted.
    /// </summary>
    public async Task<(int Id, bool IsNew)> InsertMovieFullAsync(TmdbMovieDetailFull m)
    {
        DateOnly? releaseDate = TryParseDate(m.ReleaseDate);
        short? releaseYear = (short?)releaseDate?.Year;

        // Flatten credit arrays to text
        var castList = m.Credits?.Cast
            .OrderBy(c => c.Order).Take(10)
            .Select(c => c.Name) is { } cast ? string.Join(", ", cast) : null;
        var director = m.Credits?.Crew
            .Where(c => c.Job == "Director")
            .Select(c => c.Name) is { } dirs ? string.Join(", ", dirs) : null;

        var companies = m.ProductionCompanies.Count > 0
            ? string.Join(", ", m.ProductionCompanies.Select(c => c.Name)) : null;
        var countries = m.ProductionCountries.Count > 0
            ? string.Join(", ", m.ProductionCountries.Select(c => c.Iso)) : null;
        var languages = m.SpokenLanguages.Count > 0
            ? string.Join(", ", m.SpokenLanguages.Select(c => c.Iso)) : null;

        await using var conn = await _ds.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO movies (
                tmdb_id, media_type, title, original_title, overview,
                release_year, release_date, runtime_minutes, vote_average, vote_count,
                popularity, status, original_language, is_adult, tagline,
                poster_path, imdb_id, budget, revenue, cast_list, director,
                backdrop_path, homepage, collection_id, collection_name,
                spoken_languages, production_companies, production_countries
            ) VALUES (
                $1, 'movie', $2, $3, $4,
                $5, $6, $7, $8, $9,
                $10, $11, $12, $13, $14,
                $15, $16, $17, $18, $19, $20,
                $21, $22, $23, $24,
                $25, $26, $27
            )
            ON CONFLICT (tmdb_id, media_type) DO UPDATE SET
                runtime_minutes      = COALESCE(EXCLUDED.runtime_minutes,      movies.runtime_minutes),
                status               = COALESCE(EXCLUDED.status,               movies.status),
                tagline              = COALESCE(EXCLUDED.tagline,               movies.tagline),
                imdb_id              = COALESCE(EXCLUDED.imdb_id,              movies.imdb_id),
                budget               = COALESCE(NULLIF(EXCLUDED.budget,  0),   movies.budget),
                revenue              = COALESCE(NULLIF(EXCLUDED.revenue, 0),   movies.revenue),
                cast_list            = COALESCE(EXCLUDED.cast_list,            movies.cast_list),
                director             = COALESCE(EXCLUDED.director,             movies.director),
                backdrop_path        = COALESCE(EXCLUDED.backdrop_path,        movies.backdrop_path),
                homepage             = COALESCE(EXCLUDED.homepage,             movies.homepage),
                collection_id        = COALESCE(EXCLUDED.collection_id,        movies.collection_id),
                collection_name      = COALESCE(EXCLUDED.collection_name,      movies.collection_name),
                spoken_languages     = COALESCE(EXCLUDED.spoken_languages,     movies.spoken_languages),
                production_companies = COALESCE(EXCLUDED.production_companies, movies.production_companies),
                production_countries = COALESCE(EXCLUDED.production_countries, movies.production_countries),
                vote_average         = EXCLUDED.vote_average,
                vote_count           = EXCLUDED.vote_count,
                popularity           = EXCLUDED.popularity,
                poster_path          = COALESCE(EXCLUDED.poster_path,          movies.poster_path)
            RETURNING id, (xmax = 0) AS is_new", conn);

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
        cmd.Parameters.AddWithValue(m.Budget > 0 ? (object)m.Budget : DBNull.Value);
        cmd.Parameters.AddWithValue(m.Revenue > 0 ? (object)m.Revenue : DBNull.Value);
        cmd.Parameters.AddWithValue(castList ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(director ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(m.BackdropPath ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(m.Homepage ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(m.BelongsToCollection?.Id is int cid ? (object)cid : DBNull.Value);
        cmd.Parameters.AddWithValue(m.BelongsToCollection?.Name ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(languages ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(companies ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(countries ?? (object)DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetInt32(0), reader.GetBoolean(1));
    }

    /// <summary>All TV show tmdb_ids already in the DB — used for display during dump import.</summary>
    public async Task<HashSet<int>> GetExistingTvTmdbIdsAsync()
    {
        var set = new HashSet<int>();
        await using var conn = await _ds.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT tmdb_id FROM movies WHERE media_type = 'tv'", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            set.Add(reader.GetInt32(0));
        return set;
    }

    public async Task<(int Id, bool IsNew)> InsertTvFullAsync(TmdbTvDetailFull tv)
    {
        DateOnly? airDate = TryParseDate(tv.FirstAirDate);
        short? releaseYear = (short?)airDate?.Year;
        short? runtime = tv.EpisodeRunTime?.FirstOrDefault() is int r and > 0 ? (short)r : null;

        var castList = tv.Credits?.Cast
            .OrderBy(c => c.Order).Take(10)
            .Select(c => c.Name) is { } cast ? string.Join(", ", cast) : null;

        string? creator;
        if (tv.CreatedBy.Count > 0)
            creator = string.Join(", ", tv.CreatedBy.Select(c => c.Name));
        else
        {
            var crewCreators = tv.Credits?.Crew
                .Where(c => c.Job is "Creator" or "Executive Producer")
                .Take(3)
                .Select(c => c.Name)
                .ToList();
            creator = crewCreators?.Count > 0 ? string.Join(", ", crewCreators) : null;
        }

        var companies = tv.ProductionCompanies.Count > 0
            ? string.Join(", ", tv.ProductionCompanies.Select(c => c.Name)) : null;
        var countries = tv.ProductionCountries.Count > 0
            ? string.Join(", ", tv.ProductionCountries.Select(c => c.Iso)) : null;
        var languages = tv.SpokenLanguages.Count > 0
            ? string.Join(", ", tv.SpokenLanguages.Select(c => c.Iso)) : null;

        await using var conn = await _ds.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO movies (
                tmdb_id, media_type, title, original_title, overview,
                release_year, release_date, runtime_minutes, vote_average, vote_count,
                popularity, status, original_language, is_adult,
                poster_path, cast_list, director,
                backdrop_path, spoken_languages, production_companies, production_countries
            ) VALUES (
                $1, 'tv', $2, $3, $4,
                $5, $6, $7, $8, $9,
                $10, $11, $12, false,
                $13, $14, $15,
                $16, $17, $18, $19
            )
            ON CONFLICT (tmdb_id, media_type) DO UPDATE SET
                runtime_minutes      = COALESCE(EXCLUDED.runtime_minutes,      movies.runtime_minutes),
                status               = COALESCE(EXCLUDED.status,               movies.status),
                cast_list            = COALESCE(EXCLUDED.cast_list,            movies.cast_list),
                director             = COALESCE(EXCLUDED.director,             movies.director),
                backdrop_path        = COALESCE(EXCLUDED.backdrop_path,        movies.backdrop_path),
                spoken_languages     = COALESCE(EXCLUDED.spoken_languages,     movies.spoken_languages),
                production_companies = COALESCE(EXCLUDED.production_companies, movies.production_companies),
                production_countries = COALESCE(EXCLUDED.production_countries, movies.production_countries),
                vote_average         = EXCLUDED.vote_average,
                vote_count           = EXCLUDED.vote_count,
                popularity           = EXCLUDED.popularity,
                poster_path          = COALESCE(EXCLUDED.poster_path,          movies.poster_path)
            RETURNING id, (xmax = 0) AS is_new", conn);

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
        cmd.Parameters.AddWithValue(castList ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(creator ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(tv.BackdropPath ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(languages ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(companies ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(countries ?? (object)DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetInt32(0), reader.GetBoolean(1));
    }

    public async Task<bool> TvDumpMigrationAppliedAsync()
    {
        await using var conn = await _ds.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT EXISTS(SELECT 1 FROM information_schema.tables WHERE table_name='tmdb_tv_dump_progress')",
            conn);
        return (bool)(await cmd.ExecuteScalarAsync() ?? false);
    }

    public async Task<int> GetTvDumpProgressAsync()
    {
        await using var conn = await _ds.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT last_processed_tmdb_id FROM tmdb_tv_dump_progress WHERE id = 1", conn);
        var result = await cmd.ExecuteScalarAsync();
        return result is int i ? i : 0;
    }

    public async Task UpdateTvDumpProgressAsync(int tmdbId)
    {
        await using var conn = await _ds.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            UPDATE tmdb_tv_dump_progress
            SET last_processed_tmdb_id = $1, updated_at = NOW()
            WHERE id = 1", conn);
        cmd.Parameters.AddWithValue(tmdbId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> GetDumpProgressAsync()
    {
        await using var conn = await _ds.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT last_processed_tmdb_id FROM tmdb_dump_progress WHERE id = 1", conn);
        var result = await cmd.ExecuteScalarAsync();
        return result is int i ? i : 0;
    }

    public async Task UpdateDumpProgressAsync(int tmdbId)
    {
        await using var conn = await _ds.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            UPDATE tmdb_dump_progress
            SET last_processed_tmdb_id = $1, updated_at = NOW()
            WHERE id = 1", conn);
        cmd.Parameters.AddWithValue(tmdbId);
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
