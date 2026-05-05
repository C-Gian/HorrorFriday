namespace HorrorFriday.Web.Models;

public class MovieDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? OriginalTitle { get; set; }
    public string? Overview { get; set; }
    public short? ReleaseYear { get; set; }
    public short? RuntimeMinutes { get; set; }
    public decimal? VoteAverage { get; set; }
    public int? VoteCount { get; set; }
    public decimal? Popularity { get; set; }
    public string? Status { get; set; }
    public string? OriginalLanguage { get; set; }
    public string? Tagline { get; set; }
    public string? PosterPath { get; set; }
    public string? ImdbId { get; set; }
    public List<string> Genres { get; set; } = new();
    public List<string> Keywords { get; set; } = new();
    public string? MediaType { get; set; }
}
