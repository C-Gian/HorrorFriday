using System.Text;
using Npgsql;
using Pgvector;
using HorrorFriday.API.Models;

namespace HorrorFriday.API.Services;

/// <summary>
/// Handles all movie-related database operations.
/// Builds SQL queries dynamically based on the filters the user provides.
/// </summary>
public class MovieService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly EmbeddingService _embeddingService;

    public MovieService(NpgsqlDataSource dataSource, EmbeddingService embeddingService)
    {
        _dataSource = dataSource;
        _embeddingService = embeddingService;
    }

    /// <summary>
    /// Searches for movies using the provided filters.
    /// Builds a SQL query dynamically - only adds WHERE clauses for filters
    /// that the user actually provided.
    /// </summary>
    public async Task<PagedResult<MovieDto>> SearchAsync(SearchRequest request, int? userId = null)
    {
        var conditions = new List<string>();
        var parameters = new List<NpgsqlParameter>();
        int paramIndex = 1;

        // --- Build WHERE conditions based on provided filters ---

        // Text search on title
        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            conditions.Add($"m.title ILIKE ${paramIndex}");
            parameters.Add(new NpgsqlParameter { Value = $"%{request.Query}%" });
            paramIndex++;
        }

        // Year range
        if (request.YearFrom.HasValue)
        {
            conditions.Add($"m.release_year >= ${paramIndex}");
            parameters.Add(new NpgsqlParameter { Value = (short)request.YearFrom.Value });
            paramIndex++;
        }

        if (request.YearTo.HasValue)
        {
            conditions.Add($"m.release_year <= ${paramIndex}");
            parameters.Add(new NpgsqlParameter { Value = (short)request.YearTo.Value });
            paramIndex++;
        }

        // Max runtime
        if (request.MaxRuntime.HasValue)
        {
            conditions.Add($"m.runtime_minutes <= ${paramIndex}");
            parameters.Add(new NpgsqlParameter { Value = (short)request.MaxRuntime.Value });
            paramIndex++;
        }

        // Min rating
        if (request.MinRating.HasValue)
        {
            conditions.Add($"m.vote_average >= ${paramIndex}");
            parameters.Add(new NpgsqlParameter { Value = request.MinRating.Value });
            paramIndex++;
        }

        // Max rating
        if (request.MaxRating.HasValue)
        {
            conditions.Add($"m.vote_average <= ${paramIndex}");
            parameters.Add(new NpgsqlParameter { Value = request.MaxRating.Value });
            paramIndex++;
        }

        // Min vote count
        if (request.MinVoteCount.HasValue)
        {
            conditions.Add($"m.vote_count >= ${paramIndex}");
            parameters.Add(new NpgsqlParameter { Value = request.MinVoteCount.Value });
            paramIndex++;
        }

        // Language
        if (!string.IsNullOrWhiteSpace(request.Language))
        {
            conditions.Add($"m.original_language = ${paramIndex}");
            parameters.Add(new NpgsqlParameter { Value = request.Language });
            paramIndex++;
        }

        // Include genres: movie must have ALL of these genres
        if (request.IncludeGenres?.Count > 0)
        {
            var genrePlaceholders = new List<string>();
            foreach (var genre in request.IncludeGenres)
            {
                genrePlaceholders.Add($"${paramIndex}");
                parameters.Add(new NpgsqlParameter { Value = genre });
                paramIndex++;
            }

            conditions.Add($@"m.id IN (
                SELECT mg.movie_id FROM movie_genres mg
                JOIN genres g ON g.id = mg.genre_id
                WHERE g.name IN ({string.Join(", ", genrePlaceholders)})
                GROUP BY mg.movie_id
                HAVING COUNT(DISTINCT g.name) = {request.IncludeGenres.Count}
            )");
        }

        // Exclude genres: movie must NOT have any of these genres
        if (request.ExcludeGenres?.Count > 0)
        {
            var genrePlaceholders = new List<string>();
            foreach (var genre in request.ExcludeGenres)
            {
                genrePlaceholders.Add($"${paramIndex}");
                parameters.Add(new NpgsqlParameter { Value = genre });
                paramIndex++;
            }

            conditions.Add($@"m.id NOT IN (
                SELECT mg.movie_id FROM movie_genres mg
                JOIN genres g ON g.id = mg.genre_id
                WHERE g.name IN ({string.Join(", ", genrePlaceholders)})
            )");
        }

        // Hide movies that the user has tagged with the specified statuses
        if (userId.HasValue && request.HideStatuses?.Count > 0)
        {
            var statusPlaceholders = new List<string>();
            foreach (var status in request.HideStatuses)
            {
                statusPlaceholders.Add($"${paramIndex}");
                parameters.Add(new NpgsqlParameter { Value = status });
                paramIndex++;
            }

            conditions.Add($@"m.id NOT IN (
                SELECT movie_id FROM user_movies
                WHERE user_id = ${paramIndex} AND status IN ({string.Join(", ", statusPlaceholders)})
            )");
            parameters.Add(new NpgsqlParameter { Value = userId.Value });
            paramIndex++;
        }

        // Media type (movie / tv)
        if (!string.IsNullOrWhiteSpace(request.MediaType))
        {
            conditions.Add($"m.media_type = ${paramIndex}");
            parameters.Add(new NpgsqlParameter { Value = request.MediaType });
            paramIndex++;
        }

        // Certifications (region required)
        if (!string.IsNullOrWhiteSpace(request.Region) && request.Certifications?.Count > 0)
        {
            var certPlaceholders = new List<string>();
            foreach (var cert in request.Certifications)
            {
                certPlaceholders.Add($"${paramIndex}");
                parameters.Add(new NpgsqlParameter { Value = cert });
                paramIndex++;
            }
            conditions.Add($@"m.id IN (
                SELECT movie_id FROM movie_certifications
                WHERE region = ${paramIndex} AND certification IN ({string.Join(", ", certPlaceholders)})
            )");
            parameters.Add(new NpgsqlParameter { Value = request.Region });
            paramIndex++;
        }

        // Watch providers
        if (request.ProviderIds?.Count > 0)
        {
            var providerPlaceholders = new List<string>();
            foreach (var pid in request.ProviderIds)
            {
                providerPlaceholders.Add($"${paramIndex}");
                parameters.Add(new NpgsqlParameter { Value = pid });
                paramIndex++;
            }

            if (!string.IsNullOrWhiteSpace(request.Region))
            {
                conditions.Add($@"m.id IN (
                    SELECT movie_id FROM movie_watch_providers
                    WHERE provider_id IN ({string.Join(", ", providerPlaceholders)}) AND region = ${paramIndex}
                )");
                parameters.Add(new NpgsqlParameter { Value = request.Region });
                paramIndex++;
            }
            else
            {
                conditions.Add($@"m.id IN (
                    SELECT movie_id FROM movie_watch_providers
                    WHERE provider_id IN ({string.Join(", ", providerPlaceholders)})
                )");
            }
        }

        // --- Semantic embedding ---
        // Resolved here so all filter conditions are already in the list.
        // The embedding goes into ORDER BY only (not WHERE), so it is NOT
        // added to `parameters` — it gets appended to the main query command separately.
        float[]? queryEmbedding = null;
        if (!string.IsNullOrWhiteSpace(request.SemanticQuery))
        {
            queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(request.SemanticQuery);
            if (queryEmbedding == null)
                throw new InvalidOperationException("AI search non disponibile: impossibile generare l'embedding. Verifica la chiave OpenAI.");
            conditions.Add("m.embedding IS NOT NULL");
        }

        // Build the WHERE clause
        var whereClause = conditions.Count > 0
            ? "WHERE " + string.Join(" AND ", conditions)
            : "";

        // --- Sorting ---
        // For semantic search: rank by vector similarity (embedding <=> query_vector).
        // The embedding param index is the next one after all filter params.
        string orderClause;
        if (queryEmbedding != null)
        {
            orderClause = $"ORDER BY m.embedding <=> ${paramIndex} NULLS LAST";
        }
        else
        {
            var orderBy = request.SortBy?.ToLower() switch
            {
                "rating" => "m.vote_average",
                "year" => "m.release_year",
                "title" => "m.title",
                _ => "m.popularity"
            };
            var direction = request.SortDirection?.ToLower() == "asc" ? "ASC" : "DESC";
            orderClause = $"ORDER BY {orderBy} {direction} NULLS LAST";
        }

        // --- Pagination ---
        int offset = (request.Page - 1) * request.PageSize;

        // --- Execute count query ---
        var countSql = $"SELECT COUNT(*) FROM movies m {whereClause}";
        int totalCount;
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand(countSql, conn);
            foreach (var p in parameters)
                cmd.Parameters.Add(CloneParameter(p));
            totalCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        // --- Execute main query ---
        var sql = $@"
            SELECT m.id, m.tmdb_id, m.title, m.original_title, m.overview,
                   m.release_year, m.runtime_minutes, m.vote_average, m.vote_count,
                   m.popularity, m.budget, m.revenue, m.status, m.original_language, m.tagline,
                   m.poster_path, m.backdrop_path, m.imdb_id, m.imdb_rating, m.imdb_votes,
                   m.cast_list, m.director, m.director_of_photography, m.writers,
                   m.producers, m.music_composer, m.homepage, m.collection_id,
                   m.collection_name, m.spoken_languages, m.production_companies,
                   m.production_countries, m.media_type
            FROM movies m
            {whereClause}
            {orderClause}
            LIMIT {request.PageSize} OFFSET {offset}";

        var movies = new List<MovieDto>();
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            foreach (var p in parameters)
                cmd.Parameters.Add(CloneParameter(p));
            if (queryEmbedding != null)
                cmd.Parameters.Add(new NpgsqlParameter { Value = new Vector(queryEmbedding) });

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                movies.Add(ReadMovieFromRow(reader));
            }
        }

        // Load genres and keywords for all movies in one query each
        if (movies.Count > 0)
        {
            var movieIds = movies.Select(m => m.Id).ToList();
            await LoadGenresAsync(movies, movieIds);
            await LoadKeywordsAsync(movies, movieIds);
        }

        return new PagedResult<MovieDto>
        {
            Items = movies,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }

    /// <summary>
    /// Searches for movies by semantic similarity to a query embedding.
    /// The embedding must be generated by the caller (via OpenAI).
    /// </summary>
    public async Task<List<MovieDto>> SemanticSearchAsync(float[] queryEmbedding, int limit = 20)
    {
        var sql = @"
            SELECT m.id, m.tmdb_id, m.title, m.original_title, m.overview,
                   m.release_year, m.runtime_minutes, m.vote_average, m.vote_count,
                   m.popularity, m.budget, m.revenue, m.status, m.original_language, m.tagline,
                   m.poster_path, m.backdrop_path, m.imdb_id, m.imdb_rating, m.imdb_votes,
                   m.cast_list, m.director, m.director_of_photography, m.writers,
                   m.producers, m.music_composer, m.homepage, m.collection_id,
                   m.collection_name, m.spoken_languages, m.production_companies,
                   m.production_countries, m.media_type,
                   m.embedding <=> $1 AS distance
            FROM movies m
            WHERE m.embedding IS NOT NULL
            ORDER BY m.embedding <=> $1
            LIMIT $2";

        var movies = new List<MovieDto>();
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue(new Vector(queryEmbedding));
            cmd.Parameters.AddWithValue(limit);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                movies.Add(ReadMovieFromRow(reader));
            }
        }

        if (movies.Count > 0)
        {
            var movieIds = movies.Select(m => m.Id).ToList();
            await LoadGenresAsync(movies, movieIds);
            await LoadKeywordsAsync(movies, movieIds);
        }

        return movies;
    }

    public async Task<List<SuggestionDto>> SuggestAsync(string query, int limit = 7)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2) return new();

        const string sql = @"
            SELECT id, title, release_year, media_type, poster_path
            FROM movies
            WHERE title ILIKE $1
            ORDER BY popularity DESC NULLS LAST
            LIMIT $2";

        var suggestions = new List<SuggestionDto>();
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue($"%{query}%");
        cmd.Parameters.AddWithValue(limit);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            suggestions.Add(new SuggestionDto
            {
                Id = reader.GetInt32(0),
                Title = reader.GetString(1),
                ReleaseYear = reader.IsDBNull(2) ? null : reader.GetInt16(2),
                MediaType = reader.IsDBNull(3) ? null : reader.GetString(3),
                PosterPath = reader.IsDBNull(4) ? null : reader.GetString(4)
            });
        }
        return suggestions;
    }

    public async Task<List<MovieDto>> GetSimilarAsync(int id, int limit = 12)
    {
        const string sql = @"
            SELECT m.id, m.tmdb_id, m.title, m.original_title, m.overview,
                   m.release_year, m.runtime_minutes, m.vote_average, m.vote_count,
                   m.popularity, m.budget, m.revenue, m.status, m.original_language, m.tagline,
                   m.poster_path, m.backdrop_path, m.imdb_id, m.imdb_rating, m.imdb_votes,
                   m.cast_list, m.director, m.director_of_photography, m.writers,
                   m.producers, m.music_composer, m.homepage, m.collection_id,
                   m.collection_name, m.spoken_languages, m.production_companies,
                   m.production_countries, m.media_type
            FROM movies m
            WHERE m.id != $1 AND m.embedding IS NOT NULL
            ORDER BY m.embedding <=> (SELECT embedding FROM movies WHERE id = $1)
            LIMIT $2";

        var movies = new List<MovieDto>();
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using (var efCmd = new NpgsqlCommand("SET hnsw.ef_search = 20", conn))
            await efCmd.ExecuteNonQueryAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(id);
        cmd.Parameters.AddWithValue(limit);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            movies.Add(ReadMovieFromRow(reader));

        return movies;
    }

    /// <summary>
    /// Gets a single movie by its database ID.
    /// </summary>
    public async Task<MovieDto?> GetByIdAsync(int id)
    {
        var sql = @"
            SELECT m.id, m.tmdb_id, m.title, m.original_title, m.overview,
                   m.release_year, m.runtime_minutes, m.vote_average, m.vote_count,
                   m.popularity, m.budget, m.revenue, m.status, m.original_language, m.tagline,
                   m.poster_path, m.backdrop_path, m.imdb_id, m.imdb_rating, m.imdb_votes,
                   m.cast_list, m.director, m.director_of_photography, m.writers,
                   m.producers, m.music_composer, m.homepage, m.collection_id,
                   m.collection_name, m.spoken_languages, m.production_companies,
                   m.production_countries, m.media_type
            FROM movies m
            WHERE m.id = $1";

        MovieDto? movie = null;
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue(id);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                movie = ReadMovieFromRow(reader);
            }
        }

        if (movie != null)
        {
            await LoadGenresAsync(new List<MovieDto> { movie }, new List<int> { movie.Id });
            await LoadKeywordsAsync(new List<MovieDto> { movie }, new List<int> { movie.Id });
            await LoadDetailAvailabilityAsync(movie, "IT");
        }

        return movie;
    }

    public async Task<List<string>> GetRegionsAsync()
    {
        var regions = new List<string>();
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT DISTINCT region FROM movie_watch_providers ORDER BY region", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            regions.Add(reader.GetString(0));
        return regions;
    }

    // Exact provider names as used by TMDB. Case-insensitive match.
    private static readonly HashSet<string> MajorProviderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Netflix",
        "Amazon Prime Video", "Amazon Video",
        "Disney+", "Disney Plus",
        "Max", "HBO Max",
        "Apple TV+", "Apple TV Plus", "Apple TV",
        "Paramount+", "Paramount Plus",
        "Hulu",
        "Peacock", "Peacock Premium",
        "YouTube", "YouTube Premium",
        "Google TV", "Google Play Movies",
        "Pluto TV",
        "Tubi TV", "Tubi",
        "Amazon Freevee", "Freevee",
        "Crunchyroll",
        "DAZN",
        "Tencent Video",
        "iQIYI",
        "Hotstar", "Disney+ Hotstar",
        "Sky Go", "Sky",
        "NOW", "NOW TV",
        "Showtime",
        "Canal+",
        "MUBI",
        "fuboTV",
        "Shudder",
        "BritBox",
        "discovery+", "Discovery+",
        "ESPN+",
        "Starz",
        "MGM+",
        "Rai Play",
        "TIMvision",
        "Infinity+",
    };

    public async Task<List<ProviderDto>> GetProvidersAsync(string? region)
    {
        string sql;
        if (!string.IsNullOrWhiteSpace(region))
        {
            sql = @"
                SELECT wp.id, wp.provider_name, wp.logo_path
                FROM watch_providers wp
                JOIN movie_watch_providers mwp ON mwp.provider_id = wp.id
                WHERE mwp.region = $1
                GROUP BY wp.id, wp.provider_name, wp.logo_path
                ORDER BY COUNT(DISTINCT mwp.movie_id) DESC, wp.provider_name";
        }
        else
        {
            sql = @"
                SELECT wp.id, wp.provider_name, wp.logo_path
                FROM watch_providers wp
                ORDER BY wp.provider_name";
        }

        var providers = new List<ProviderDto>();
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        if (!string.IsNullOrWhiteSpace(region))
            cmd.Parameters.AddWithValue(region);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(1);
            if (!MajorProviderNames.Contains(name)) continue;
            providers.Add(new ProviderDto
            {
                Id = reader.GetInt32(0),
                Name = name,
                LogoPath = reader.IsDBNull(2) ? null : reader.GetString(2)
            });
        }
        return providers;
    }

    // Known valid certifications per region. Filters out garbage data (festival tags, language codes, etc.)
    private static readonly Dictionary<string, HashSet<string>> KnownCertifications =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["US"] = new(StringComparer.OrdinalIgnoreCase) { "G", "PG", "PG-13", "R", "NC-17", "NR", "TV-Y", "TV-Y7", "TV-G", "TV-PG", "TV-14", "TV-MA" },
        ["GB"] = new(StringComparer.OrdinalIgnoreCase) { "U", "PG", "12", "12A", "15", "18", "R18" },
        ["IT"] = new(StringComparer.OrdinalIgnoreCase) { "T", "VM14", "VM18" },
        ["DE"] = new(StringComparer.OrdinalIgnoreCase) { "0", "6", "12", "16", "18" },
        ["FR"] = new(StringComparer.OrdinalIgnoreCase) { "U", "10", "12", "16", "18" },
        ["ES"] = new(StringComparer.OrdinalIgnoreCase) { "APTA", "7", "12", "16", "18" },
        ["AU"] = new(StringComparer.OrdinalIgnoreCase) { "G", "PG", "M", "MA 15+", "R 18+", "X 18+" },
        ["CA"] = new(StringComparer.OrdinalIgnoreCase) { "G", "PG", "14A", "18A", "R", "A" },
        ["JP"] = new(StringComparer.OrdinalIgnoreCase) { "G", "PG12", "R15+", "R18+" },
        ["KR"] = new(StringComparer.OrdinalIgnoreCase) { "All", "12", "15", "18" },
        ["BR"] = new(StringComparer.OrdinalIgnoreCase) { "L", "10", "12", "14", "16", "18" },
        ["MX"] = new(StringComparer.OrdinalIgnoreCase) { "AA", "A", "B", "B15", "C", "D" },
        ["IN"] = new(StringComparer.OrdinalIgnoreCase) { "U", "UA", "A", "S" },
        ["NL"] = new(StringComparer.OrdinalIgnoreCase) { "AL", "6", "9", "12", "14", "16", "18" },
        ["NZ"] = new(StringComparer.OrdinalIgnoreCase) { "G", "PG", "M", "R13", "R15", "R16", "R18" },
        ["RU"] = new(StringComparer.OrdinalIgnoreCase) { "0+", "6+", "12+", "16+", "18+" },
        ["SE"] = new(StringComparer.OrdinalIgnoreCase) { "BTL", "7", "11", "15" },
        ["NO"] = new(StringComparer.OrdinalIgnoreCase) { "A", "6", "9", "12", "15", "18" },
        ["FI"] = new(StringComparer.OrdinalIgnoreCase) { "S", "7", "12", "16", "18" },
        ["DK"] = new(StringComparer.OrdinalIgnoreCase) { "A", "7", "11", "15" },
        ["PL"] = new(StringComparer.OrdinalIgnoreCase) { "AP", "7", "12", "15", "18" },
        ["CH"] = new(StringComparer.OrdinalIgnoreCase) { "0", "6", "12", "14", "16", "18" },
        ["AT"] = new(StringComparer.OrdinalIgnoreCase) { "Alle", "6", "10", "12", "14", "16" },
        ["BE"] = new(StringComparer.OrdinalIgnoreCase) { "AL", "KNN", "KNMA", "KN6", "KN9", "KN12", "KN16" },
        ["TR"] = new(StringComparer.OrdinalIgnoreCase) { "G", "7+", "13+", "18+" },
        ["SG"] = new(StringComparer.OrdinalIgnoreCase) { "G", "PG", "PG13", "NC16", "M18", "R21" },
        ["ZA"] = new(StringComparer.OrdinalIgnoreCase) { "A", "PG", "7-9PG", "10-12PG", "13", "16", "18", "X18" },
        ["AR"] = new(StringComparer.OrdinalIgnoreCase) { "ATP", "+13", "+16", "+18" },
        ["IE"] = new(StringComparer.OrdinalIgnoreCase) { "G", "PG", "12", "12A", "15A", "16", "18" },
        ["PT"] = new(StringComparer.OrdinalIgnoreCase) { "Para todos os públicos", "M/6", "M/12", "M/14", "M/16", "M/18" },
        ["HU"] = new(StringComparer.OrdinalIgnoreCase) { "KN", "6", "12", "16", "18" },
        ["RO"] = new(StringComparer.OrdinalIgnoreCase) { "AG", "AP", "12", "15", "18", "18X" },
        ["TH"] = new(StringComparer.OrdinalIgnoreCase) { "P", "G", "13+", "15+", "18+", "20+" },
        ["PH"] = new(StringComparer.OrdinalIgnoreCase) { "G", "PG", "R-13", "R-16", "R-18", "X" },
        ["MY"] = new(StringComparer.OrdinalIgnoreCase) { "U", "PG13", "18SG", "18SX", "18PA", "18" },
        ["ID"] = new(StringComparer.OrdinalIgnoreCase) { "SU", "BO", "R", "D" },
        ["HK"] = new(StringComparer.OrdinalIgnoreCase) { "I", "IIA", "IIB", "III" },
        ["TW"] = new(StringComparer.OrdinalIgnoreCase) { "0+", "6+", "12+", "15+", "18+" },
    };

    public async Task<List<string>> GetCertificationsAsync(string region)
    {
        var allCerts = new List<string>();
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT DISTINCT certification FROM movie_certifications WHERE region = $1", conn);
        cmd.Parameters.AddWithValue(region);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            allCerts.Add(reader.GetString(0));

        // Filter to known certifications for this region if we have a whitelist.
        // For unknown regions fall back to a basic sanity check (short, starts uppercase).
        if (KnownCertifications.TryGetValue(region, out var whitelist))
            return allCerts.Where(c => whitelist.Contains(c)).OrderBy(c => c).ToList();

        return allCerts
            .Where(c => c.Length <= 10 && c.Length >= 1 && char.IsUpper(c[0]))
            .OrderBy(c => c)
            .ToList();
    }

    /// <summary>
    /// Returns all available genres in the database.
    /// </summary>
    public async Task<List<string>> GetAllGenresAsync()
    {
        var genres = new List<string>();
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("SELECT name FROM genres ORDER BY name", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            genres.Add(reader.GetString(0));
        }
        return genres;
    }

    // --- Private helper methods ---

    /// <summary>
    /// Reads a single movie row from a data reader.
    /// Maps database columns to MovieDto properties.
    /// </summary>
    private MovieDto ReadMovieFromRow(NpgsqlDataReader reader)
    {
        return new MovieDto
        {
            Id = reader.GetInt32(reader.GetOrdinal("id")),
            TmdbId = reader.IsDBNull(reader.GetOrdinal("tmdb_id")) ? null : reader.GetInt32(reader.GetOrdinal("tmdb_id")),
            Title = reader.GetString(reader.GetOrdinal("title")),
            OriginalTitle = reader.IsDBNull(reader.GetOrdinal("original_title")) ? null : reader.GetString(reader.GetOrdinal("original_title")),
            Overview = reader.IsDBNull(reader.GetOrdinal("overview")) ? null : reader.GetString(reader.GetOrdinal("overview")),
            ReleaseYear = reader.IsDBNull(reader.GetOrdinal("release_year")) ? null : reader.GetInt16(reader.GetOrdinal("release_year")),
            RuntimeMinutes = reader.IsDBNull(reader.GetOrdinal("runtime_minutes")) ? null : reader.GetInt16(reader.GetOrdinal("runtime_minutes")),
            VoteAverage = reader.IsDBNull(reader.GetOrdinal("vote_average")) ? null : reader.GetDecimal(reader.GetOrdinal("vote_average")),
            VoteCount = reader.IsDBNull(reader.GetOrdinal("vote_count")) ? null : reader.GetInt32(reader.GetOrdinal("vote_count")),
            Popularity = reader.IsDBNull(reader.GetOrdinal("popularity")) ? null : reader.GetDecimal(reader.GetOrdinal("popularity")),
            Budget = reader.IsDBNull(reader.GetOrdinal("budget")) ? null : reader.GetInt64(reader.GetOrdinal("budget")),
            Revenue = reader.IsDBNull(reader.GetOrdinal("revenue")) ? null : reader.GetInt64(reader.GetOrdinal("revenue")),
            Status = reader.IsDBNull(reader.GetOrdinal("status")) ? null : reader.GetString(reader.GetOrdinal("status")),
            OriginalLanguage = reader.IsDBNull(reader.GetOrdinal("original_language")) ? null : reader.GetString(reader.GetOrdinal("original_language")),
            Tagline = reader.IsDBNull(reader.GetOrdinal("tagline")) ? null : reader.GetString(reader.GetOrdinal("tagline")),
            PosterPath = reader.IsDBNull(reader.GetOrdinal("poster_path")) ? null : reader.GetString(reader.GetOrdinal("poster_path")),
            BackdropPath = reader.IsDBNull(reader.GetOrdinal("backdrop_path")) ? null : reader.GetString(reader.GetOrdinal("backdrop_path")),
            ImdbId = reader.IsDBNull(reader.GetOrdinal("imdb_id")) ? null : reader.GetString(reader.GetOrdinal("imdb_id")),
            ImdbRating = reader.IsDBNull(reader.GetOrdinal("imdb_rating")) ? null : reader.GetDecimal(reader.GetOrdinal("imdb_rating")),
            ImdbVotes = reader.IsDBNull(reader.GetOrdinal("imdb_votes")) ? null : reader.GetInt32(reader.GetOrdinal("imdb_votes")),
            CastList = reader.IsDBNull(reader.GetOrdinal("cast_list")) ? null : reader.GetString(reader.GetOrdinal("cast_list")),
            Director = reader.IsDBNull(reader.GetOrdinal("director")) ? null : reader.GetString(reader.GetOrdinal("director")),
            DirectorOfPhotography = reader.IsDBNull(reader.GetOrdinal("director_of_photography")) ? null : reader.GetString(reader.GetOrdinal("director_of_photography")),
            Writers = reader.IsDBNull(reader.GetOrdinal("writers")) ? null : reader.GetString(reader.GetOrdinal("writers")),
            Producers = reader.IsDBNull(reader.GetOrdinal("producers")) ? null : reader.GetString(reader.GetOrdinal("producers")),
            MusicComposer = reader.IsDBNull(reader.GetOrdinal("music_composer")) ? null : reader.GetString(reader.GetOrdinal("music_composer")),
            Homepage = reader.IsDBNull(reader.GetOrdinal("homepage")) ? null : reader.GetString(reader.GetOrdinal("homepage")),
            CollectionId = reader.IsDBNull(reader.GetOrdinal("collection_id")) ? null : reader.GetInt32(reader.GetOrdinal("collection_id")),
            CollectionName = reader.IsDBNull(reader.GetOrdinal("collection_name")) ? null : reader.GetString(reader.GetOrdinal("collection_name")),
            SpokenLanguages = reader.IsDBNull(reader.GetOrdinal("spoken_languages")) ? null : reader.GetString(reader.GetOrdinal("spoken_languages")),
            ProductionCompanies = reader.IsDBNull(reader.GetOrdinal("production_companies")) ? null : reader.GetString(reader.GetOrdinal("production_companies")),
            ProductionCountries = reader.IsDBNull(reader.GetOrdinal("production_countries")) ? null : reader.GetString(reader.GetOrdinal("production_countries")),
            MediaType = reader.IsDBNull(reader.GetOrdinal("media_type")) ? null : reader.GetString(reader.GetOrdinal("media_type"))
        };
    }

    private async Task LoadDetailAvailabilityAsync(MovieDto movie, string region)
    {
        const string providerSql = @"
            SELECT wp.id, wp.provider_name, wp.logo_path, mwp.region, mwp.provider_type
            FROM movie_watch_providers mwp
            JOIN watch_providers wp ON wp.id = mwp.provider_id
            WHERE mwp.movie_id = $1 AND mwp.region = $2
            ORDER BY
                CASE mwp.provider_type
                    WHEN 'stream' THEN 1
                    WHEN 'ads' THEN 2
                    WHEN 'rent' THEN 3
                    WHEN 'buy' THEN 4
                    ELSE 5
                END,
                wp.provider_name";

        const string certificationSql = @"
            SELECT region, certification
            FROM movie_certifications
            WHERE movie_id = $1 AND region = $2
            ORDER BY certification";

        await using var conn = await _dataSource.OpenConnectionAsync();

        await using (var providerCmd = new NpgsqlCommand(providerSql, conn))
        {
            providerCmd.Parameters.AddWithValue(movie.Id);
            providerCmd.Parameters.AddWithValue(region);
            await using var reader = await providerCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                movie.WatchProviders.Add(new MovieWatchProviderDto
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    LogoPath = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Region = reader.GetString(3),
                    Type = reader.GetString(4)
                });
            }
        }

        await using (var certificationCmd = new NpgsqlCommand(certificationSql, conn))
        {
            certificationCmd.Parameters.AddWithValue(movie.Id);
            certificationCmd.Parameters.AddWithValue(region);
            await using var reader = await certificationCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                movie.Certifications.Add(new MovieCertificationDto
                {
                    Region = reader.GetString(0),
                    Certification = reader.GetString(1)
                });
            }
        }
    }

    /// <summary>
    /// Loads genres for a batch of movies in a single query.
    /// Instead of querying genres for each movie individually (N+1 problem),
    /// we load all genres for all movies at once and distribute them.
    /// </summary>
    private async Task LoadGenresAsync(List<MovieDto> movies, List<int> movieIds)
    {
        var placeholders = string.Join(", ", movieIds.Select((_, i) => $"${i + 1}"));
        var sql = $@"
            SELECT mg.movie_id, g.name
            FROM movie_genres mg
            JOIN genres g ON g.id = mg.genre_id
            WHERE mg.movie_id IN ({placeholders})";

        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var id in movieIds)
            cmd.Parameters.AddWithValue(id);

        var genreMap = new Dictionary<int, List<string>>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var movieId = reader.GetInt32(0);
            var genreName = reader.GetString(1);
            if (!genreMap.ContainsKey(movieId))
                genreMap[movieId] = new List<string>();
            genreMap[movieId].Add(genreName);
        }

        foreach (var movie in movies)
        {
            if (genreMap.TryGetValue(movie.Id, out var genres))
                movie.Genres = genres;
        }
    }

    /// <summary>
    /// Loads keywords for a batch of movies in a single query.
    /// Same approach as LoadGenresAsync to avoid N+1 queries.
    /// </summary>
    private async Task LoadKeywordsAsync(List<MovieDto> movies, List<int> movieIds)
    {
        var placeholders = string.Join(", ", movieIds.Select((_, i) => $"${i + 1}"));
        var sql = $@"
            SELECT mk.movie_id, k.name
            FROM movie_keywords mk
            JOIN keywords k ON k.id = mk.keyword_id
            WHERE mk.movie_id IN ({placeholders})";

        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var id in movieIds)
            cmd.Parameters.AddWithValue(id);

        var keywordMap = new Dictionary<int, List<string>>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var movieId = reader.GetInt32(0);
            var keywordName = reader.GetString(1);
            if (!keywordMap.ContainsKey(movieId))
                keywordMap[movieId] = new List<string>();
            keywordMap[movieId].Add(keywordName);
        }

        foreach (var movie in movies)
        {
            if (keywordMap.TryGetValue(movie.Id, out var keywords))
                movie.Keywords = keywords;
        }
    }

    /// <summary>
    /// Creates a copy of a parameter. Needed because the same parameter
    /// object can't be added to two different commands.
    /// </summary>
    private NpgsqlParameter CloneParameter(NpgsqlParameter source)
    {
        return new NpgsqlParameter { Value = source.Value };
    }
}
