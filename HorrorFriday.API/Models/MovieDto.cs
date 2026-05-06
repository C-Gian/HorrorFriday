namespace HorrorFriday.API.Models;

/// <summary>
/// Data Transfer Object for a movie.
/// A DTO is a simplified version of the database record,
/// containing only the fields the frontend needs to display.
/// "Dto" means "Data Transfer Object" - it's the shape of the data
/// that travels between backend and frontend.
/// </summary>
public class MovieDto
{
    public int Id { get; set; }
    public int? TmdbId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? OriginalTitle { get; set; }
    public string? Overview { get; set; }
    public short? ReleaseYear { get; set; }
    public short? RuntimeMinutes { get; set; }
    public decimal? VoteAverage { get; set; }
    public int? VoteCount { get; set; }
    public decimal? Popularity { get; set; }
    public long? Budget { get; set; }
    public long? Revenue { get; set; }
    public string? Status { get; set; }
    public string? OriginalLanguage { get; set; }
    public string? Tagline { get; set; }
    public string? PosterPath { get; set; }
    public string? BackdropPath { get; set; }
    public string? ImdbId { get; set; }
    public decimal? ImdbRating { get; set; }
    public int? ImdbVotes { get; set; }
    public string? CastList { get; set; }
    public string? Director { get; set; }
    public string? DirectorOfPhotography { get; set; }
    public string? Writers { get; set; }
    public string? Producers { get; set; }
    public string? MusicComposer { get; set; }
    public string? Homepage { get; set; }
    public int? CollectionId { get; set; }
    public string? CollectionName { get; set; }
    public string? SpokenLanguages { get; set; }
    public string? ProductionCompanies { get; set; }
    public string? ProductionCountries { get; set; }
    public List<string> Genres { get; set; } = new();
    public List<string> Keywords { get; set; } = new();
    public List<MovieWatchProviderDto> WatchProviders { get; set; } = new();
    public List<MovieCertificationDto> Certifications { get; set; } = new();
    public string? MediaType { get; set; }
}

public class MovieWatchProviderDto
{
    public int Id { get; set; }
    public int? TmdbId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? LogoPath { get; set; }
    public string Region { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}

public class MovieCertificationDto
{
    public string Region { get; set; } = string.Empty;
    public string Certification { get; set; } = string.Empty;
}

public class SuggestionDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public short? ReleaseYear { get; set; }
    public string? MediaType { get; set; }
    public string? PosterPath { get; set; }
}
