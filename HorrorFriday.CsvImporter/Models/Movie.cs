namespace HorrorFriday.CsvImporter.Models;

public class Movie
{
    public int TmdbId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string OriginalTitle { get; set; } = string.Empty;
    public string Overview { get; set; } = string.Empty;
    public short? ReleaseYear { get; set; }
    public DateOnly? ReleaseDate { get; set; }
    public short? RuntimeMinutes { get; set; }
    public decimal? VoteAverage { get; set; }
    public int? VoteCount { get; set; }
    public decimal? Popularity { get; set; }
    public string Status { get; set; } = string.Empty;
    public string OriginalLanguage { get; set; } = string.Empty;
    public bool IsAdult { get; set; }
    public string? Tagline { get; set; }
    public string? PosterPath { get; set; }
    public string? ImdbId { get; set; }
    public List<string> Genres { get; set; } = new();
    public List<string> Keywords { get; set; } = new();
    public float[]? Embedding { get; set; }
}
