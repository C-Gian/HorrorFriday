namespace HorrorFriday.API.Models;

public class UserStatsDto
{
    public int TotalInLibrary { get; set; }
    public int TotalWatched { get; set; }
    public int TotalWatching { get; set; }
    public int TotalToWatch { get; set; }
    public int TotalDropped { get; set; }
    public decimal? AverageRating { get; set; }
    public Dictionary<int, int> RatingDistribution { get; set; } = new();
    public List<GenreCountDto> TopGenres { get; set; } = new();
    public List<MonthlyActivityDto> MonthlyActivity { get; set; } = new();
}

public class GenreCountDto
{
    public string Genre { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class MonthlyActivityDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int Count { get; set; }
}
