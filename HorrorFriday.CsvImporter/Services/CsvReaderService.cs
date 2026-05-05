using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using HorrorFriday.CsvImporter.Models;

namespace HorrorFriday.CsvImporter.Services;

public class CsvReaderService
{
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
            if (movie != null) yield return movie;
        }
    }

    private static Movie? ConvertToMovie(CsvMovieRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.Overview)) return null;
        var validStatuses = new[] { "Released", "Post Production", "In Production" };
        if (!validStatuses.Any(s => string.Equals(record.Status, s, StringComparison.OrdinalIgnoreCase))) return null;
        if (!int.TryParse(record.VoteCount, out int voteCount) || voteCount == 0) return null;
        bool isAdult = string.Equals(record.Adult, "True", StringComparison.OrdinalIgnoreCase);
        if (isAdult) return null;

        DateOnly? releaseDate = null;
        short? releaseYear = null;
        if (DateOnly.TryParse(record.ReleaseDate, out var pd)) { releaseDate = pd; releaseYear = (short)pd.Year; }

        short? runtime = null;
        if (short.TryParse(record.Runtime, out var pr) && pr > 0) runtime = pr;

        decimal? voteAverage = null;
        if (decimal.TryParse(record.VoteAverage, NumberStyles.Any, CultureInfo.InvariantCulture, out var pv)) voteAverage = pv;

        decimal? popularity = null;
        if (decimal.TryParse(record.Popularity, NumberStyles.Any, CultureInfo.InvariantCulture, out var pp)) popularity = pp;

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
            Genres = ParseList(record.Genres),
            Keywords = ParseList(record.Keywords)
        };
    }

    private static List<string> ParseList(string input) =>
        string.IsNullOrWhiteSpace(input) ? [] :
        input.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToList();
}
