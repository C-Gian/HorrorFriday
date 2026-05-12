using Npgsql;
using HorrorFriday.API.Models;

namespace HorrorFriday.API.Services;

public class UserMovieService
{
    private readonly NpgsqlDataSource _dataSource;

    public UserMovieService(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<Dictionary<int, string>> GetStatusesAsync(int userId, List<int> movieIds)
    {
        if (movieIds.Count == 0) return new();

        var placeholders = string.Join(", ", movieIds.Select((_, i) => $"${i + 2}"));
        var sql = $@"
            SELECT um.movie_id, um.status
            FROM user_movies um
            JOIN movies m ON m.id = um.movie_id
            WHERE um.user_id = $1
              AND COALESCE(m.is_adult, false) = false
              AND um.movie_id IN ({placeholders})";

        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(userId);
        foreach (var id in movieIds)
            cmd.Parameters.AddWithValue(id);

        var result = new Dictionary<int, string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result[Convert.ToInt32(reader["movie_id"])] = reader["status"].ToString()!;

        return result;
    }

    public async Task UpsertStatusAsync(int userId, int movieId, string status)
    {
        const string sql = @"
            INSERT INTO user_movies (user_id, movie_id, status, added_at, updated_at)
            SELECT $1, m.id, $3, NOW(), NOW()
            FROM movies m
            WHERE m.id = $2 AND COALESCE(m.is_adult, false) = false
            ON CONFLICT (user_id, movie_id) DO UPDATE SET status = $3, updated_at = NOW()";

        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(userId);
        cmd.Parameters.AddWithValue(movieId);
        cmd.Parameters.AddWithValue(status);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpsertEntryAsync(int userId, int movieId, UpdateUserMovieRequest request)
    {
        const string sql = @"
            INSERT INTO user_movies (user_id, movie_id, status, user_rating, notes, added_at, updated_at)
            SELECT $1, m.id, $3, $4, $5, NOW(), NOW()
            FROM movies m
            WHERE m.id = $2 AND COALESCE(m.is_adult, false) = false
            ON CONFLICT (user_id, movie_id) DO UPDATE SET
                status = $3,
                user_rating = CASE WHEN $6 THEN NULL ELSE COALESCE($4, user_movies.user_rating) END,
                notes = CASE WHEN $7 THEN NULL ELSE COALESCE($5, user_movies.notes) END,
                updated_at = NOW()";

        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(userId);
        cmd.Parameters.AddWithValue(movieId);
        cmd.Parameters.AddWithValue(request.Status);
        cmd.Parameters.AddWithValue((object?)request.UserRating ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)request.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue(request.ClearUserRating);
        cmd.Parameters.AddWithValue(request.ClearNotes);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<UserMovieEntryDto?> GetEntryAsync(int userId, int movieId)
    {
        const string sql = @"
            SELECT movie_id, status, user_rating, notes, added_at, updated_at
            FROM user_movies um
            JOIN movies m ON m.id = um.movie_id
            WHERE um.user_id = $1
              AND um.movie_id = $2
              AND COALESCE(m.is_adult, false) = false";

        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(userId);
        cmd.Parameters.AddWithValue(movieId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new UserMovieEntryDto
        {
            MovieId = reader.GetInt32(0),
            Status = reader.GetString(1),
            UserRating = reader.IsDBNull(2) ? null : reader.GetInt16(2),
            Notes = reader.IsDBNull(3) ? null : reader.GetString(3),
            AddedAt = reader.GetDateTime(4),
            UpdatedAt = reader.GetDateTime(5)
        };
    }

    public async Task<List<UserMovieLibraryItemDto>> GetLibraryAsync(int userId, string? status)
    {
        var sql = @"
            SELECT um.status, um.user_rating, um.notes, um.added_at, um.updated_at,
                   m.id, m.tmdb_id, m.title, m.original_title, m.overview,
                   m.release_year, m.runtime_minutes, m.vote_average, m.vote_count,
                   m.popularity, m.status AS movie_status, m.original_language, m.tagline,
                   m.poster_path, m.imdb_id
            FROM user_movies um
            JOIN movies m ON m.id = um.movie_id
            WHERE um.user_id = $1
              AND COALESCE(m.is_adult, false) = false";

        if (!string.IsNullOrWhiteSpace(status))
            sql += " AND um.status = $2";

        sql += " ORDER BY um.updated_at DESC";

        var result = new List<UserMovieLibraryItemDto>();
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(userId);
        if (!string.IsNullOrWhiteSpace(status))
            cmd.Parameters.AddWithValue(status);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new UserMovieLibraryItemDto
            {
                Status = reader.GetString(0),
                UserRating = reader.IsDBNull(1) ? null : reader.GetInt16(1),
                Notes = reader.IsDBNull(2) ? null : reader.GetString(2),
                AddedAt = reader.GetDateTime(3),
                UpdatedAt = reader.GetDateTime(4),
                Movie = new MovieDto
                {
                    Id = reader.GetInt32(5),
                    Title = reader.GetString(7),
                    OriginalTitle = reader.IsDBNull(8) ? null : reader.GetString(8),
                    Overview = reader.IsDBNull(9) ? null : reader.GetString(9),
                    ReleaseYear = reader.IsDBNull(10) ? null : reader.GetInt16(10),
                    RuntimeMinutes = reader.IsDBNull(11) ? null : reader.GetInt16(11),
                    VoteAverage = reader.IsDBNull(12) ? null : reader.GetDecimal(12),
                    VoteCount = reader.IsDBNull(13) ? null : reader.GetInt32(13),
                    Popularity = reader.IsDBNull(14) ? null : reader.GetDecimal(14),
                    Status = reader.IsDBNull(15) ? null : reader.GetString(15),
                    OriginalLanguage = reader.IsDBNull(16) ? null : reader.GetString(16),
                    Tagline = reader.IsDBNull(17) ? null : reader.GetString(17),
                    PosterPath = reader.IsDBNull(18) ? null : reader.GetString(18),
                    ImdbId = reader.IsDBNull(19) ? null : reader.GetString(19)
                }
            });
        }

        return result;
    }

    public async Task RemoveAsync(int userId, int movieId)
    {
        const string sql = "DELETE FROM user_movies WHERE user_id = $1 AND movie_id = $2";

        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(userId);
        cmd.Parameters.AddWithValue(movieId);
        await cmd.ExecuteNonQueryAsync();
    }
}
