namespace HorrorFriday.Importer.Models;

/// <summary>
/// Rappresenta un film "pulito", pronto per essere salvato nel database.
/// I dati vengono convertiti dal CsvMovieRecord grezzo a questo formato tipizzato.
/// </summary>
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

    /// <summary>
    /// Lista dei generi del film (es. ["Action", "Science Fiction"])
    /// </summary>
    public List<string> Genres { get; set; } = new();

    /// <summary>
    /// Lista delle keyword (es. ["rescue", "dream", "heist"])
    /// </summary>
    public List<string> Keywords { get; set; } = new();

    /// <summary>
    /// L'embedding generato da OpenAI — un array di 1536 numeri decimali
    /// che rappresenta il "significato" della trama del film.
    /// Sarà null finché non viene generato.
    /// </summary>
    public float[]? Embedding { get; set; }
}