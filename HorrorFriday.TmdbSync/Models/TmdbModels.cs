using System.Text.Json.Serialization;

namespace HorrorFriday.TmdbSync.Models;

// ─── Paged discover response ────────────────────────────────────────────────

public class TmdbPagedResult<T>
{
    public int Page { get; init; }
    public List<T> Results { get; init; } = [];
    [JsonPropertyName("total_pages")] public int TotalPages { get; init; }
    [JsonPropertyName("total_results")] public int TotalResults { get; init; }
}

// ─── Discover: movies ────────────────────────────────────────────────────────

public class TmdbMovieResult
{
    public int Id { get; init; }
    public string? Title { get; init; }
    [JsonPropertyName("original_title")] public string? OriginalTitle { get; init; }
    public string? Overview { get; init; }
    [JsonPropertyName("release_date")] public string? ReleaseDate { get; init; }
    [JsonPropertyName("poster_path")] public string? PosterPath { get; init; }
    public bool Adult { get; init; }
    [JsonPropertyName("original_language")] public string? OriginalLanguage { get; init; }
    [JsonPropertyName("genre_ids")] public List<int> GenreIds { get; init; } = [];
    [JsonPropertyName("vote_average")] public double VoteAverage { get; init; }
    [JsonPropertyName("vote_count")] public int VoteCount { get; init; }
    public double Popularity { get; init; }
}

// ─── Discover: TV shows ──────────────────────────────────────────────────────

public class TmdbTvResult
{
    public int Id { get; init; }
    public string? Name { get; init; }
    [JsonPropertyName("original_name")] public string? OriginalName { get; init; }
    public string? Overview { get; init; }
    [JsonPropertyName("first_air_date")] public string? FirstAirDate { get; init; }
    [JsonPropertyName("poster_path")] public string? PosterPath { get; init; }
    [JsonPropertyName("original_language")] public string? OriginalLanguage { get; init; }
    [JsonPropertyName("genre_ids")] public List<int> GenreIds { get; init; } = [];
    [JsonPropertyName("vote_average")] public double VoteAverage { get; init; }
    [JsonPropertyName("vote_count")] public int VoteCount { get; init; }
    public double Popularity { get; init; }
}

// ─── Movie detail (with release_dates + watch/providers appended) ────────────

public class TmdbMovieDetails
{
    public int Id { get; init; }
    public string? Title { get; init; }
    [JsonPropertyName("original_title")] public string? OriginalTitle { get; init; }
    public string? Overview { get; init; }
    [JsonPropertyName("release_date")] public string? ReleaseDate { get; init; }
    [JsonPropertyName("poster_path")] public string? PosterPath { get; init; }
    public bool Adult { get; init; }
    [JsonPropertyName("original_language")] public string? OriginalLanguage { get; init; }
    public int Runtime { get; init; }
    [JsonPropertyName("vote_average")] public double VoteAverage { get; init; }
    [JsonPropertyName("vote_count")] public int VoteCount { get; init; }
    public double Popularity { get; init; }
    public string? Status { get; init; }
    public string? Tagline { get; init; }
    [JsonPropertyName("imdb_id")] public string? ImdbId { get; init; }
    [JsonPropertyName("release_dates")] public TmdbReleaseDatesWrapper? ReleaseDates { get; init; }
    [JsonPropertyName("watch/providers")] public TmdbWatchProvidersWrapper? WatchProviders { get; init; }
}

// ─── TV detail (with content_ratings + watch/providers appended) ─────────────

public class TmdbTvDetails
{
    public int Id { get; init; }
    public string? Name { get; init; }
    [JsonPropertyName("original_name")] public string? OriginalName { get; init; }
    public string? Overview { get; init; }
    [JsonPropertyName("first_air_date")] public string? FirstAirDate { get; init; }
    [JsonPropertyName("poster_path")] public string? PosterPath { get; init; }
    [JsonPropertyName("original_language")] public string? OriginalLanguage { get; init; }
    [JsonPropertyName("episode_run_time")] public List<int>? EpisodeRunTime { get; init; }
    [JsonPropertyName("vote_average")] public double VoteAverage { get; init; }
    [JsonPropertyName("vote_count")] public int VoteCount { get; init; }
    public double Popularity { get; init; }
    public string? Status { get; init; }
    [JsonPropertyName("content_ratings")] public TmdbContentRatingsWrapper? ContentRatings { get; init; }
    [JsonPropertyName("watch/providers")] public TmdbWatchProvidersWrapper? WatchProviders { get; init; }
}

// ─── Release dates / certifications ─────────────────────────────────────────

public class TmdbReleaseDatesWrapper
{
    public List<TmdbReleaseDateCountry> Results { get; init; } = [];
}

public class TmdbReleaseDateCountry
{
    [JsonPropertyName("iso_3166_1")] public string Region { get; init; } = "";
    [JsonPropertyName("release_dates")] public List<TmdbReleaseDate> ReleaseDates { get; init; } = [];
}

public class TmdbReleaseDate
{
    public string Certification { get; init; } = "";
    public int Type { get; init; }
}

// ─── TV content ratings ──────────────────────────────────────────────────────

public class TmdbContentRatingsWrapper
{
    public List<TmdbContentRating> Results { get; init; } = [];
}

public class TmdbContentRating
{
    [JsonPropertyName("iso_3166_1")] public string Region { get; init; } = "";
    public string Rating { get; init; } = "";
}

// ─── Watch providers ─────────────────────────────────────────────────────────

public class TmdbWatchProvidersWrapper
{
    public Dictionary<string, TmdbRegionProviders> Results { get; init; } = [];
}

public class TmdbRegionProviders
{
    public string? Link { get; init; }
    [JsonPropertyName("flatrate")] public List<TmdbProvider>? Stream { get; init; }
    public List<TmdbProvider>? Rent { get; init; }
    public List<TmdbProvider>? Buy { get; init; }
    public List<TmdbProvider>? Ads { get; init; }
}

public class TmdbProvider
{
    [JsonPropertyName("provider_id")] public int ProviderId { get; init; }
    [JsonPropertyName("provider_name")] public string ProviderName { get; init; } = "";
    [JsonPropertyName("logo_path")] public string? LogoPath { get; init; }
}

// ─── Internal: extracted metadata ready for DB write ────────────────────────

public record MovieMetadata(
    List<(string Region, string Certification)> Certifications,
    TmdbWatchProvidersWrapper? WatchProviders
);

// ─── Sync statistics ─────────────────────────────────────────────────────────

public class SyncStats
{
    public int NewMoviesInserted { get; set; }
    public int NewTvInserted { get; set; }
    public int CertificationsUpdated { get; set; }
    public int ProvidersUpdated { get; set; }
    public int Errors { get; set; }
    public int Skipped { get; set; }
}

// ─── App settings (read from appsettings.json) ───────────────────────────────

public class SyncSettings
{
    public int DelayBetweenRequestsMs { get; set; } = 250;
    public int DailyLookbackDays { get; set; } = 7;
    public int BackfillBatchSize { get; set; } = 500;
    public string[] CertificationRegions { get; set; } = ["US", "IT", "GB", "DE", "FR"];
    public string[] ProvidersRegions { get; set; } = ["IT", "US", "GB", "DE", "FR"];
    public int[] HorrorGenreIds { get; set; } = [];
    public CsvPathsSettings CsvPaths { get; set; } = new();
}

public class CsvPathsSettings
{
    public string OldCsv { get; set; } = "";
    public string NewCsv { get; set; } = "";
    public string DiffOutputDir { get; set; } = "Data/csv-diff";
}
