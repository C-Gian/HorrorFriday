namespace HorrorFriday.API.Models;

/// <summary>
/// Represents the search filters sent by the frontend.
/// All fields are optional - the user can combine any filters they want.
/// </summary>
public class SearchRequest
{
    /// <summary>
    /// Text search query (matches title)
    /// </summary>
    public string? Query { get; set; }

    /// <summary>
    /// Semantic search query in natural language
    /// (e.g. "a slow atmospheric horror set in a house")
    /// </summary>
    public string? SemanticQuery { get; set; }

    /// <summary>
    /// Filter by genres to include (e.g. ["Horror", "Thriller"])
    /// </summary>
    public List<string>? IncludeGenres { get; set; }

    /// <summary>
    /// Filter by genres to exclude (e.g. ["Comedy"])
    /// </summary>
    public List<string>? ExcludeGenres { get; set; }

    /// <summary>
    /// Minimum release year
    /// </summary>
    public int? YearFrom { get; set; }

    /// <summary>
    /// Maximum release year
    /// </summary>
    public int? YearTo { get; set; }

    /// <summary>
    /// Maximum runtime in minutes
    /// </summary>
    public int? MaxRuntime { get; set; }

    /// <summary>
    /// Minimum vote average (e.g. 7.0)
    /// </summary>
    public decimal? MinRating { get; set; }

    /// <summary>
    /// Maximum vote average (e.g. 6.0)
    /// </summary>
    public decimal? MaxRating { get; set; }

    /// <summary>
    /// Minimum number of votes (to filter out obscure movies)
    /// </summary>
    public int? MinVoteCount { get; set; }

    /// <summary>
    /// Filter by original language (e.g. "en", "it", "ja")
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// How to sort results: "popularity", "rating", "year", "title"
    /// </summary>
    public string? SortBy { get; set; }

    /// <summary>
    /// Sort direction: "asc" or "desc"
    /// </summary>
    public string? SortDirection { get; set; }

    /// <summary>
    /// Page number for pagination (starts at 1)
    /// </summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// Number of results per page
    /// </summary>
    public int PageSize { get; set; } = 20;

    /// <summary>
    /// Exclude movies that the authenticated user has tagged with these statuses.
    /// Valid values: "to_watch", "watching", "watched", "dropped".
    /// Populated by the frontend; the controller attaches the userId from JWT.
    /// </summary>
    public List<string>? HideStatuses { get; set; }

    /// <summary>
    /// Filter by media type: "movie", "tv", or null for both.
    /// </summary>
    public string? MediaType { get; set; }

    /// <summary>
    /// ISO 3166-1 region code (e.g. "IT", "US"). Used together with Certifications and ProviderIds.
    /// </summary>
    public string? Region { get; set; }

    /// <summary>
    /// Filter by age certification codes (e.g. ["R", "PG-13"]). Region must be set.
    /// </summary>
    public List<string>? Certifications { get; set; }

    /// <summary>
    /// Filter to titles available on these watch provider IDs. Optionally scoped to Region.
    /// </summary>
    public List<int>? ProviderIds { get; set; }
}
