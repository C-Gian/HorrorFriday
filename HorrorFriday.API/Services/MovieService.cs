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

        // --- Semantic embedding ---
        // Resolved here so all filter conditions are already in the list.
        // The embedding goes into ORDER BY only (not WHERE), so it is NOT
        // added to `parameters` — it gets appended to the main query command separately.
        float[]? queryEmbedding = null;
        if (!string.IsNullOrWhiteSpace(request.SemanticQuery))
        {
            queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(request.SemanticQuery);
            if (queryEmbedding != null)
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
                   m.popularity, m.status, m.original_language, m.tagline,
                   m.poster_path, m.imdb_id
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
                   m.popularity, m.status, m.original_language, m.tagline,
                   m.poster_path, m.imdb_id,
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

    /// <summary>
    /// Gets a single movie by its database ID.
    /// </summary>
    public async Task<MovieDto?> GetByIdAsync(int id)
    {
        var sql = @"
            SELECT m.id, m.tmdb_id, m.title, m.original_title, m.overview,
                   m.release_year, m.runtime_minutes, m.vote_average, m.vote_count,
                   m.popularity, m.status, m.original_language, m.tagline,
                   m.poster_path, m.imdb_id
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
        }

        return movie;
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
            Title = reader.GetString(reader.GetOrdinal("title")),
            OriginalTitle = reader.IsDBNull(reader.GetOrdinal("original_title")) ? null : reader.GetString(reader.GetOrdinal("original_title")),
            Overview = reader.IsDBNull(reader.GetOrdinal("overview")) ? null : reader.GetString(reader.GetOrdinal("overview")),
            ReleaseYear = reader.IsDBNull(reader.GetOrdinal("release_year")) ? null : reader.GetInt16(reader.GetOrdinal("release_year")),
            RuntimeMinutes = reader.IsDBNull(reader.GetOrdinal("runtime_minutes")) ? null : reader.GetInt16(reader.GetOrdinal("runtime_minutes")),
            VoteAverage = reader.IsDBNull(reader.GetOrdinal("vote_average")) ? null : reader.GetDecimal(reader.GetOrdinal("vote_average")),
            VoteCount = reader.IsDBNull(reader.GetOrdinal("vote_count")) ? null : reader.GetInt32(reader.GetOrdinal("vote_count")),
            Popularity = reader.IsDBNull(reader.GetOrdinal("popularity")) ? null : reader.GetDecimal(reader.GetOrdinal("popularity")),
            Status = reader.IsDBNull(reader.GetOrdinal("status")) ? null : reader.GetString(reader.GetOrdinal("status")),
            OriginalLanguage = reader.IsDBNull(reader.GetOrdinal("original_language")) ? null : reader.GetString(reader.GetOrdinal("original_language")),
            Tagline = reader.IsDBNull(reader.GetOrdinal("tagline")) ? null : reader.GetString(reader.GetOrdinal("tagline")),
            PosterPath = reader.IsDBNull(reader.GetOrdinal("poster_path")) ? null : reader.GetString(reader.GetOrdinal("poster_path")),
            ImdbId = reader.IsDBNull(reader.GetOrdinal("imdb_id")) ? null : reader.GetString(reader.GetOrdinal("imdb_id"))
        };
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