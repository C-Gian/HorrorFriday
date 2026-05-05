using System.Text.Json.Serialization;

namespace HorrorFriday.TmdbImporter.Models;

public class TmdbPagedResult<T>
{
    public int Page { get; init; }
    public List<T> Results { get; init; } = [];
    [JsonPropertyName("total_pages")] public int TotalPages { get; init; }
    [JsonPropertyName("total_results")] public int TotalResults { get; init; }
}

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

// Used by Modes 1–4 (certifications + providers only, no credits)
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

// ─── Full detail model — used by Mode [5] dump import (includes credits, collection, etc.) ──

public class TmdbMovieDetailFull
{
    public int Id { get; init; }
    public string? Title { get; init; }
    [JsonPropertyName("original_title")] public string? OriginalTitle { get; init; }
    public string? Overview { get; init; }
    [JsonPropertyName("release_date")] public string? ReleaseDate { get; init; }
    [JsonPropertyName("poster_path")] public string? PosterPath { get; init; }
    [JsonPropertyName("backdrop_path")] public string? BackdropPath { get; init; }
    public bool Adult { get; init; }
    public bool Video { get; init; }
    [JsonPropertyName("original_language")] public string? OriginalLanguage { get; init; }
    public int Runtime { get; init; }
    [JsonPropertyName("vote_average")] public double VoteAverage { get; init; }
    [JsonPropertyName("vote_count")] public int VoteCount { get; init; }
    public double Popularity { get; init; }
    public string? Status { get; init; }
    public string? Tagline { get; init; }
    [JsonPropertyName("imdb_id")] public string? ImdbId { get; init; }
    public string? Homepage { get; init; }
    public long Budget { get; init; }
    public long Revenue { get; init; }
    public List<TmdbGenreDetail> Genres { get; init; } = [];
    [JsonPropertyName("belongs_to_collection")] public TmdbCollection? BelongsToCollection { get; init; }
    [JsonPropertyName("production_companies")] public List<TmdbProductionCompany> ProductionCompanies { get; init; } = [];
    [JsonPropertyName("production_countries")] public List<TmdbProductionCountry> ProductionCountries { get; init; } = [];
    [JsonPropertyName("spoken_languages")] public List<TmdbSpokenLanguage> SpokenLanguages { get; init; } = [];
    public TmdbCredits? Credits { get; init; }
    [JsonPropertyName("release_dates")] public TmdbReleaseDatesWrapper? ReleaseDates { get; init; }
    [JsonPropertyName("watch/providers")] public TmdbWatchProvidersWrapper? WatchProviders { get; init; }
}

public class TmdbCollection
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
}

public class TmdbGenreDetail
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
}

public class TmdbProductionCompany
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    [JsonPropertyName("origin_country")] public string OriginCountry { get; init; } = "";
}

public class TmdbProductionCountry
{
    [JsonPropertyName("iso_3166_1")] public string Iso { get; init; } = "";
    public string Name { get; init; } = "";
}

public class TmdbSpokenLanguage
{
    [JsonPropertyName("iso_639_1")] public string Iso { get; init; } = "";
    public string Name { get; init; } = "";
}

public class TmdbCredits
{
    public List<TmdbCastMember> Cast { get; init; } = [];
    public List<TmdbCrewMember> Crew { get; init; } = [];
}

public class TmdbCastMember
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string? Character { get; init; }
    public int Order { get; init; }
}

public class TmdbCrewMember
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Job { get; init; } = "";
    public string Department { get; init; } = "";
}

// ─── Dump file line formats ───────────────────────────────────────────────────

public class TmdbDumpEntry
{
    public int Id { get; init; }
    public bool Adult { get; init; }
    public bool Video { get; init; }
    public double Popularity { get; init; }
    [JsonPropertyName("original_title")] public string? OriginalTitle { get; init; }
}

public class TmdbTvDumpEntry
{
    public int Id { get; init; }
    public bool Adult { get; init; }
    public double Popularity { get; init; }
    [JsonPropertyName("original_name")] public string? OriginalName { get; init; }
}

// ─── Full TV detail model — used by Mode [6] TV dump import ──────────────────

public class TmdbTvDetailFull
{
    public int Id { get; init; }
    public string? Name { get; init; }
    [JsonPropertyName("original_name")] public string? OriginalName { get; init; }
    public string? Overview { get; init; }
    [JsonPropertyName("first_air_date")] public string? FirstAirDate { get; init; }
    [JsonPropertyName("poster_path")] public string? PosterPath { get; init; }
    [JsonPropertyName("backdrop_path")] public string? BackdropPath { get; init; }
    [JsonPropertyName("original_language")] public string? OriginalLanguage { get; init; }
    [JsonPropertyName("episode_run_time")] public List<int>? EpisodeRunTime { get; init; }
    [JsonPropertyName("vote_average")] public double VoteAverage { get; init; }
    [JsonPropertyName("vote_count")] public int VoteCount { get; init; }
    public double Popularity { get; init; }
    public string? Status { get; init; }
    [JsonPropertyName("created_by")] public List<TmdbCreator> CreatedBy { get; init; } = [];
    public List<TmdbGenreDetail> Genres { get; init; } = [];
    [JsonPropertyName("production_companies")] public List<TmdbProductionCompany> ProductionCompanies { get; init; } = [];
    [JsonPropertyName("production_countries")] public List<TmdbProductionCountry> ProductionCountries { get; init; } = [];
    [JsonPropertyName("spoken_languages")] public List<TmdbSpokenLanguage> SpokenLanguages { get; init; } = [];
    public TmdbCredits? Credits { get; init; }
    [JsonPropertyName("content_ratings")] public TmdbContentRatingsWrapper? ContentRatings { get; init; }
    [JsonPropertyName("watch/providers")] public TmdbWatchProvidersWrapper? WatchProviders { get; init; }
}

public class TmdbCreator
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
}

// ─── Shared API wrappers ──────────────────────────────────────────────────────

public class TmdbReleaseDatesWrapper { public List<TmdbReleaseDateCountry> Results { get; init; } = []; }
public class TmdbReleaseDateCountry
{
    [JsonPropertyName("iso_3166_1")] public string Region { get; init; } = "";
    [JsonPropertyName("release_dates")] public List<TmdbReleaseDate> ReleaseDates { get; init; } = [];
}
public class TmdbReleaseDate { public string Certification { get; init; } = ""; public int Type { get; init; } }

public class TmdbContentRatingsWrapper { public List<TmdbContentRating> Results { get; init; } = []; }
public class TmdbContentRating
{
    [JsonPropertyName("iso_3166_1")] public string Region { get; init; } = "";
    public string Rating { get; init; } = "";
}

public class TmdbWatchProvidersWrapper { public Dictionary<string, TmdbRegionProviders> Results { get; init; } = []; }
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

public record MovieMetadata(
    List<(string Region, string Certification)> Certifications,
    TmdbWatchProvidersWrapper? WatchProviders
);

// ─── Stats & Settings ─────────────────────────────────────────────────────────

public class SyncStats
{
    public int NewMoviesInserted { get; set; }
    public int NewTvInserted { get; set; }
    public int CertificationsUpdated { get; set; }
    public int ProvidersUpdated { get; set; }
    public int Errors { get; set; }
    public int Skipped { get; set; }
    public int Enriched { get; set; }
}

public class SyncSettings
{
    public int DelayBetweenRequestsMs { get; set; } = 250;
    public int DailyLookbackDays { get; set; } = 7;
    public int BackfillBatchSize { get; set; } = 500;
    public string[] CertificationRegions { get; set; } = ["US", "IT", "GB", "DE", "FR"];
    public string[] ProvidersRegions { get; set; } = ["IT", "US", "GB", "DE", "FR"];
    public int[] HorrorGenreIds { get; set; } = [];

    // Mode [5] dump import filters
    public double MinPopularity { get; set; } = 0.3;
    public int MinRuntimeMinutes { get; set; } = 40;
    public int DumpCheckpointEvery { get; set; } = 100;
    public int DumpParallelism { get; set; } = 15;
    public int DumpMaxRequestsPerSecond { get; set; } = 15;

    // Mode [7] embedding backfill
    public int EmbeddingBatchSize { get; set; } = 100;
}
