using Npgsql;
using HorrorFriday.API.Models;

namespace HorrorFriday.API.Services;

public class UserStatsService
{
    private readonly NpgsqlDataSource _dataSource;

    public UserStatsService(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<UserStatsDto> GetStatsAsync(int userId)
    {
        var stats = new UserStatsDto();
        await using var conn = await _dataSource.OpenConnectionAsync();

        // Status counts
        await using (var cmd = new NpgsqlCommand(
            "SELECT status, COUNT(*) FROM user_movies WHERE user_id = $1 GROUP BY status", conn))
        {
            cmd.Parameters.AddWithValue(userId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var status = r.GetString(0);
                var count = (int)r.GetInt64(1);
                stats.TotalInLibrary += count;
                switch (status)
                {
                    case "watched":  stats.TotalWatched  = count; break;
                    case "watching": stats.TotalWatching = count; break;
                    case "to_watch": stats.TotalToWatch  = count; break;
                    case "dropped":  stats.TotalDropped  = count; break;
                }
            }
        }

        // Rating distribution + average
        await using (var cmd = new NpgsqlCommand(
            @"SELECT user_rating, COUNT(*) FROM user_movies
              WHERE user_id = $1 AND user_rating IS NOT NULL
              GROUP BY user_rating ORDER BY user_rating", conn))
        {
            cmd.Parameters.AddWithValue(userId);
            await using var r = await cmd.ExecuteReaderAsync();
            long totalRatings = 0, sumRatings = 0;
            while (await r.ReadAsync())
            {
                var rating = r.GetInt32(0);
                var count = (int)r.GetInt64(1);
                stats.RatingDistribution[rating] = count;
                totalRatings += count;
                sumRatings += (long)rating * count;
            }
            if (totalRatings > 0)
                stats.AverageRating = Math.Round((decimal)sumRatings / totalRatings, 1);
        }

        // Top genres (from entire library)
        await using (var cmd = new NpgsqlCommand(
            @"SELECT g.name, COUNT(*) AS cnt
              FROM user_movies um
              JOIN movie_genres mg ON mg.movie_id = um.movie_id
              JOIN genres g ON g.id = mg.genre_id
              WHERE um.user_id = $1
              GROUP BY g.name ORDER BY cnt DESC LIMIT 8", conn))
        {
            cmd.Parameters.AddWithValue(userId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                stats.TopGenres.Add(new GenreCountDto
                {
                    Genre = r.GetString(0),
                    Count = (int)r.GetInt64(1)
                });
            }
        }

        // Monthly activity — last 13 months
        await using (var cmd = new NpgsqlCommand(
            @"SELECT EXTRACT(YEAR FROM added_at)::int,
                     EXTRACT(MONTH FROM added_at)::int,
                     COUNT(*)::int
              FROM user_movies
              WHERE user_id = $1 AND added_at >= NOW() - INTERVAL '13 months'
              GROUP BY 1, 2 ORDER BY 1, 2", conn))
        {
            cmd.Parameters.AddWithValue(userId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                stats.MonthlyActivity.Add(new MonthlyActivityDto
                {
                    Year  = r.GetInt32(0),
                    Month = r.GetInt32(1),
                    Count = r.GetInt32(2)
                });
            }
        }

        return stats;
    }
}
