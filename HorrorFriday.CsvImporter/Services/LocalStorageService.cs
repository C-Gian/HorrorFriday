using System.Globalization;
using System.Text;
using HorrorFriday.CsvImporter.Models;

namespace HorrorFriday.CsvImporter.Services;

public class LocalStorageService
{
    private readonly string _outputDir;
    private readonly Dictionary<string, int> _genres = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _keywords = new(StringComparer.OrdinalIgnoreCase);
    private int _nextGenreId = 1;
    private int _nextKeywordId = 1;
    private readonly List<(int movieId, int genreId)> _movieGenres = new();
    private readonly List<(int movieId, int keywordId)> _movieKeywords = new();
    private int _nextMovieId = 1;

    public LocalStorageService(string outputDir)
    {
        _outputDir = outputDir;
        Directory.CreateDirectory(outputDir);
    }

    public int AddMovie(Movie movie)
    {
        int movieId = _nextMovieId++;
        foreach (var g in movie.Genres)
        {
            if (!_genres.ContainsKey(g)) _genres[g] = _nextGenreId++;
            _movieGenres.Add((movieId, _genres[g]));
        }
        foreach (var k in movie.Keywords)
        {
            if (!_keywords.ContainsKey(k)) _keywords[k] = _nextKeywordId++;
            _movieKeywords.Add((movieId, _keywords[k]));
        }
        return movieId;
    }

    public void WriteAllFiles(List<(int id, Movie movie)> movies)
    {
        Console.WriteLine("Scrittura file locali...");
        WriteMoviesFile(movies);
        WriteFile("genres.tsv", _genres.OrderBy(g => g.Value).Select(g => $"{g.Value}\t{Escape(g.Key)}"));
        WriteFile("keywords.tsv", _keywords.OrderBy(k => k.Value).Select(k => $"{k.Value}\t{Escape(k.Key)}"));
        WriteFile("movie_genres.tsv", _movieGenres.Select(x => $"{x.movieId}\t{x.genreId}"));
        WriteFile("movie_keywords.tsv", _movieKeywords.Select(x => $"{x.movieId}\t{x.keywordId}"));
        Console.WriteLine($"  File scritti in: {_outputDir}");
        Console.WriteLine($"  Film: {movies.Count} | Generi: {_genres.Count} | Keyword: {_keywords.Count}");
    }

    private void WriteMoviesFile(List<(int id, Movie movie)> movies)
    {
        using var w = new StreamWriter(Path.Combine(_outputDir, "movies.tsv"), false, Encoding.UTF8);
        foreach (var (id, m) in movies)
        {
            var emb = m.Embedding != null
                ? "[" + string.Join(",", m.Embedding.Select(f => f.ToString(CultureInfo.InvariantCulture))) + "]"
                : @"\N";
            w.WriteLine(string.Join("\t", new[]
            {
                id.ToString(), m.TmdbId.ToString(), Escape(m.Title), Escape(m.OriginalTitle),
                Escape(m.Overview), m.ReleaseYear?.ToString() ?? @"\N",
                m.ReleaseDate?.ToString("yyyy-MM-dd") ?? @"\N",
                m.RuntimeMinutes?.ToString() ?? @"\N",
                m.VoteAverage?.ToString(CultureInfo.InvariantCulture) ?? @"\N",
                m.VoteCount?.ToString() ?? @"\N",
                m.Popularity?.ToString(CultureInfo.InvariantCulture) ?? @"\N",
                Escape(m.Status), Escape(m.OriginalLanguage),
                m.IsAdult ? "t" : "f",
                m.Tagline != null ? Escape(m.Tagline) : @"\N",
                m.PosterPath ?? @"\N", m.ImdbId ?? @"\N", emb
            }));
        }
    }

    private void WriteFile(string name, IEnumerable<string> lines)
    {
        using var w = new StreamWriter(Path.Combine(_outputDir, name), false, Encoding.UTF8);
        foreach (var line in lines) w.WriteLine(line);
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return @"\N";
        return value.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "\\r");
    }
}
