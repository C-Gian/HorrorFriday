using CsvHelper.Configuration.Attributes;

namespace HorrorFriday.CsvImporter.Models;

public class CsvMovieRecord
{
    [Name("id")] public int Id { get; set; }
    [Name("title")] public string Title { get; set; } = string.Empty;
    [Name("original_title")] public string OriginalTitle { get; set; } = string.Empty;
    [Name("overview")] public string Overview { get; set; } = string.Empty;
    [Name("release_date")] public string ReleaseDate { get; set; } = string.Empty;
    [Name("runtime")] public string Runtime { get; set; } = string.Empty;
    [Name("vote_average")] public string VoteAverage { get; set; } = string.Empty;
    [Name("vote_count")] public string VoteCount { get; set; } = string.Empty;
    [Name("popularity")] public string Popularity { get; set; } = string.Empty;
    [Name("status")] public string Status { get; set; } = string.Empty;
    [Name("adult")] public string Adult { get; set; } = string.Empty;
    [Name("original_language")] public string OriginalLanguage { get; set; } = string.Empty;
    [Name("poster_path")] public string PosterPath { get; set; } = string.Empty;
    [Name("backdrop_path")] public string BackdropPath { get; set; } = string.Empty;
    [Name("tagline")] public string Tagline { get; set; } = string.Empty;
    [Name("imdb_id")] public string ImdbId { get; set; } = string.Empty;
    [Name("homepage")] public string Homepage { get; set; } = string.Empty;
    [Name("budget")] public string Budget { get; set; } = string.Empty;
    [Name("revenue")] public string Revenue { get; set; } = string.Empty;
    [Name("genres")] public string Genres { get; set; } = string.Empty;
    [Name("keywords")] public string Keywords { get; set; } = string.Empty;
    [Name("production_companies")] public string ProductionCompanies { get; set; } = string.Empty;
    [Name("production_countries")] public string ProductionCountries { get; set; } = string.Empty;
    [Name("spoken_languages")] public string SpokenLanguages { get; set; } = string.Empty;
}
