using System.Globalization;
using System.Text;
using HorrorFriday.Importer.Models;

namespace HorrorFriday.Importer.Services;

/// <summary>
/// Saves processed movie data to local files on disk.
/// These files are then bulk-loaded into the database using PostgreSQL COPY,
/// which is orders of magnitude faster than individual INSERT statements.
/// 
/// The workflow is:
/// 1. Process movies (parse CSV + generate embeddings) → save to local files
/// 2. Bulk-load the local files into the database in one shot
/// </summary>
public class LocalStorageService
{
    private readonly string _outputDir;

    // Track unique genres and keywords with their assigned IDs
    private readonly Dictionary<string, int> _genres = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _keywords = new(StringComparer.OrdinalIgnoreCase);
    private int _nextGenreId = 1;
    private int _nextKeywordId = 1;

    // Track all movie-genre and movie-keyword links
    private readonly List<(int movieId, int genreId)> _movieGenres = new();
    private readonly List<(int movieId, int keywordId)> _movieKeywords = new();

    // Auto-incrementing movie ID
    private int _nextMovieId = 1;

    public LocalStorageService(string outputDir)
    {
        _outputDir = outputDir;
        Directory.CreateDirectory(outputDir);
    }

    /// <summary>
    /// Processes a movie and stores it in memory.
    /// Returns the assigned movie ID.
    /// </summary>
    public int AddMovie(Movie movie)
    {
        int movieId = _nextMovieId++;

        // Track genres
        foreach (var genreName in movie.Genres)
        {
            if (!_genres.ContainsKey(genreName))
                _genres[genreName] = _nextGenreId++;

            _movieGenres.Add((movieId, _genres[genreName]));
        }

        // Track keywords
        foreach (var keywordName in movie.Keywords)
        {
            if (!_keywords.ContainsKey(keywordName))
                _keywords[keywordName] = _nextKeywordId++;

            _movieKeywords.Add((movieId, _keywords[keywordName]));
        }

        return movieId;
    }

    /// <summary>
    /// Writes all collected data to tab-separated files ready for COPY import.
    /// PostgreSQL COPY expects tab-separated values with \N for nulls.
    /// </summary>
    public void WriteAllFiles(List<(int id, Movie movie)> movies)
    {
        Console.WriteLine("Writing local files...");

        WriteMoviesFile(movies);
        WriteGenresFile();
        WriteKeywordsFile();
        WriteMovieGenresFile();
        WriteMovieKeywordsFile();

        Console.WriteLine($"Files written to: {_outputDir}");
        Console.WriteLine($"  Movies:         {movies.Count}");
        Console.WriteLine($"  Genres:         {_genres.Count}");
        Console.WriteLine($"  Keywords:       {_keywords.Count}");
        Console.WriteLine($"  Movie-Genres:   {_movieGenres.Count}");
        Console.WriteLine($"  Movie-Keywords: {_movieKeywords.Count}");
    }

    private void WriteMoviesFile(List<(int id, Movie movie)> movies)
    {
        var path = Path.Combine(_outputDir, "movies.tsv");
        using var writer = new StreamWriter(path, false, Encoding.UTF8);

        foreach (var (id, movie) in movies)
        {
            // Format embedding as PostgreSQL vector literal: [0.1,0.2,0.3]
            var embeddingStr = movie.Embedding != null
                ? "[" + string.Join(",", movie.Embedding.Select(f => f.ToString(CultureInfo.InvariantCulture))) + "]"
                : @"\N";

            var fields = new[]
            {
                id.ToString(),
                movie.TmdbId.ToString(),
                Escape(movie.Title),
                Escape(movie.OriginalTitle),
                Escape(movie.Overview),
                movie.ReleaseYear?.ToString() ?? @"\N",
                movie.ReleaseDate?.ToString("yyyy-MM-dd") ?? @"\N",
                movie.RuntimeMinutes?.ToString() ?? @"\N",
                movie.VoteAverage?.ToString(CultureInfo.InvariantCulture) ?? @"\N",
                movie.VoteCount?.ToString() ?? @"\N",
                movie.Popularity?.ToString(CultureInfo.InvariantCulture) ?? @"\N",
                Escape(movie.Status),
                Escape(movie.OriginalLanguage),
                movie.IsAdult ? "t" : "f",
                movie.Tagline != null ? Escape(movie.Tagline) : @"\N",
                movie.PosterPath ?? @"\N",
                movie.ImdbId ?? @"\N",
                embeddingStr
            };

            writer.WriteLine(string.Join("\t", fields));
        }
    }

    private void WriteGenresFile()
    {
        var path = Path.Combine(_outputDir, "genres.tsv");
        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        foreach (var (name, id) in _genres.OrderBy(g => g.Value))
        {
            writer.WriteLine($"{id}\t{Escape(name)}");
        }
    }

    private void WriteKeywordsFile()
    {
        var path = Path.Combine(_outputDir, "keywords.tsv");
        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        foreach (var (name, id) in _keywords.OrderBy(k => k.Value))
        {
            writer.WriteLine($"{id}\t{Escape(name)}");
        }
    }

    private void WriteMovieGenresFile()
    {
        var path = Path.Combine(_outputDir, "movie_genres.tsv");
        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        foreach (var (movieId, genreId) in _movieGenres)
        {
            writer.WriteLine($"{movieId}\t{genreId}");
        }
    }

    private void WriteMovieKeywordsFile()
    {
        var path = Path.Combine(_outputDir, "movie_keywords.tsv");
        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        foreach (var (movieId, keywordId) in _movieKeywords)
        {
            writer.WriteLine($"{movieId}\t{keywordId}");
        }
    }

    /// <summary>
    /// Escapes special characters for PostgreSQL COPY format.
    /// Tabs, newlines and backslashes must be escaped.
    /// </summary>
    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return @"\N";

        return value
            .Replace("\\", "\\\\")
            .Replace("\t", "\\t")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");
    }
}