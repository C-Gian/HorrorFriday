using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using HorrorFriday.Importer.Models;

namespace HorrorFriday.Importer.Services;

/// <summary>
/// Legge il CSV di Kaggle e converte ogni riga in un oggetto Movie pulito.
/// Filtra i film inutili (senza trama, senza voti, non ancora usciti).
/// </summary>
public class CsvReaderService
{
    /// <summary>
    /// Legge il CSV e restituisce solo i film validi, uno alla volta.
    /// 
    /// Usa "yield return" per non caricare tutto il file in memoria —
    /// con 1 milione di righe, caricare tutto insieme mangerebbe troppa RAM.
    /// Invece, restituisce un film alla volta: lo legge, lo converte, lo passa avanti.
    /// </summary>
    public IEnumerable<Movie> ReadMovies(string csvFilePath)
    {
        using var reader = new StreamReader(csvFilePath);

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            MissingFieldFound = null,
            BadDataFound = null,
            Mode = CsvMode.RFC4180
        };

        using var csv = new CsvReader(reader, config);

        foreach (var record in csv.GetRecords<CsvMovieRecord>())
        {
            var movie = ConvertToMovie(record);

            if (movie != null)
            {
                yield return movie;
            }
        }
    }

    /// <summary>
    /// Converte una riga grezza del CSV in un oggetto Movie pulito.
    /// Restituisce null se il film non è valido (manca la trama, non è uscito, ecc.)
    /// </summary>
    private Movie? ConvertToMovie(CsvMovieRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.Overview))
            return null;

        var validStatuses = new[] { "Released", "Post Production", "In Production" };
        if (!validStatuses.Any(s => string.Equals(record.Status, s, StringComparison.OrdinalIgnoreCase)))
            return null;

        if (!int.TryParse(record.VoteCount, out int voteCount) || voteCount == 0)
            return null;

        bool isAdult = string.Equals(record.Adult, "True", StringComparison.OrdinalIgnoreCase);
        if (isAdult)
            return null;

        DateOnly? releaseDate = null;
        short? releaseYear = null;
        if (DateOnly.TryParse(record.ReleaseDate, out var parsedDate))
        {
            releaseDate = parsedDate;
            releaseYear = (short)parsedDate.Year;
        }

        short? runtime = null;
        if (short.TryParse(record.Runtime, out var parsedRuntime) && parsedRuntime > 0)
        {
            runtime = parsedRuntime;
        }

        decimal? voteAverage = null;
        if (decimal.TryParse(record.VoteAverage, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedVote))
        {
            voteAverage = parsedVote;
        }

        decimal? popularity = null;
        if (decimal.TryParse(record.Popularity, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedPop))
        {
            popularity = parsedPop;
        }

        var genres = ParseCommaSeparatedList(record.Genres);

        var keywords = ParseCommaSeparatedList(record.Keywords);

        return new Movie
        {
            TmdbId = record.Id,
            Title = record.Title.Trim(),
            OriginalTitle = record.OriginalTitle.Trim(),
            Overview = record.Overview.Trim(),
            ReleaseDate = releaseDate,
            ReleaseYear = releaseYear,
            RuntimeMinutes = runtime,
            VoteAverage = voteAverage,
            VoteCount = voteCount,
            Popularity = popularity,
            Status = record.Status.Trim(),
            OriginalLanguage = record.OriginalLanguage.Trim(),
            IsAdult = isAdult,
            Tagline = string.IsNullOrWhiteSpace(record.Tagline) ? null : record.Tagline.Trim(),
            PosterPath = string.IsNullOrWhiteSpace(record.PosterPath) ? null : record.PosterPath.Trim(),
            ImdbId = string.IsNullOrWhiteSpace(record.ImdbId) ? null : record.ImdbId.Trim(),
            Genres = genres,
            Keywords = keywords
        };
    }

    /// <summary>
    /// Prende una stringa tipo "Action, Science Fiction, Adventure"
    /// e la trasforma in una lista: ["Action", "Science Fiction", "Adventure"].
    /// Rimuove spazi extra e valori vuoti.
    /// </summary>
    private List<string> ParseCommaSeparatedList(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new List<string>();

        return input
            .Split(',')
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct() 
            .ToList();
    }
}