using System.Diagnostics;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using Pgvector;
using Pgvector.Npgsql;

namespace HorrorFriday.CsvImporter.Services;

public sealed class NewRecordsImportService : IDisposable
{
    private readonly NpgsqlDataSource _ds;
    private readonly EmbeddingService _embedder;
    private readonly string _diffFilePath;
    private readonly string _newCsvPath;

    private static class Col
    {
        public const int Id = 0;
        public const int Title = 1;
        public const int VoteAverage = 2;
        public const int VoteCount = 3;
        public const int Status = 4;
        public const int ReleaseDate = 5;
        public const int Revenue = 6;
        public const int Runtime = 7;
        public const int Budget = 8;
        public const int ImdbId = 9;
        public const int OriginalLanguage = 10;
        public const int OriginalTitle = 11;
        public const int Overview = 12;
        public const int Popularity = 13;
        public const int Tagline = 14;
        public const int Genres = 15;
        public const int Cast = 19;
        public const int Director = 20;
        public const int Dop = 21;
        public const int Writers = 22;
        public const int Producers = 23;
        public const int MusicComposer = 24;
        public const int ImdbRating = 25;
        public const int ImdbVotes = 26;
        public const int PosterPath = 27;
    }

    public NewRecordsImportService(string connectionString, string openAiKey, string diffFilePath, string newCsvPath)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UseVector();
        _ds = builder.Build();
        _embedder = new EmbeddingService(openAiKey);
        _diffFilePath = diffFilePath;
        _newCsvPath = newCsvPath;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        Console.WriteLine("Caricamento ID da diff_only_in_new.txt...");
        var targetIds = await LoadTargetIdsAsync();
        Console.WriteLine($"  {targetIds.Count:N0} record da importare.\n");

        if (targetIds.Count == 0) { Console.WriteLine("  Nessun ID trovato. Operazione non necessaria."); return; }

        Console.WriteLine("Caricamento generi dal DB...");
        var genreMap = await LoadGenreMapAsync();
        Console.WriteLine($"  {genreMap.Count} generi caricati.\n");

        Console.WriteLine("Lettura CSV, embedding e inserimento...");
        var (inserted, skipped, embedErrors) = await ProcessAsync(targetIds, genreMap, ct);

        sw.Stop();
        var elapsed = sw.Elapsed.ToString(@"hh\:mm\:ss");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"""

  ┌─ Import nuovi record completato ─────────────────────────┐
  │  Record inseriti        : {inserted,8:N0}                      │
  │  Già presenti (saltati) : {skipped,8:N0}                      │
  │  Errori embedding       : {embedErrors,8:N0}                      │
  │  Tempo totale           : {elapsed}                    │
  └──────────────────────────────────────────────────────────┘
""");
        Console.ResetColor();
    }

    private async Task<HashSet<int>> LoadTargetIdsAsync()
    {
        var ids = new HashSet<int>();
        foreach (var line in await File.ReadAllLinesAsync(_diffFilePath))
            if (int.TryParse(line.Trim(), out int id)) ids.Add(id);
        return ids;
    }

    private async Task<Dictionary<string, int>> LoadGenreMapAsync()
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using var conn = await _ds.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("SELECT id, name FROM genres", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) map[r.GetString(1)] = r.GetInt32(0);
        return map;
    }

    private async Task<(int inserted, int skipped, int embedErrors)> ProcessAsync(
        HashSet<int> targetIds, Dictionary<string, int> genreMap, CancellationToken ct)
    {
        const int BatchSize = 50;
        var batch = new List<string[]>(BatchSize);
        int inserted = 0, skipped = 0, embedErrors = 0;
        long fileSize = new FileInfo(_newCsvPath).Length;
        await using var conn = await _ds.OpenConnectionAsync();

        await foreach (var fields in ParseCsvAsync(_newCsvPath, fileSize))
        {
            ct.ThrowIfCancellationRequested();
            if (fields.Length <= Col.PosterPath) continue;
            if (!int.TryParse(fields[Col.Id], out int id)) continue;
            if (!targetIds.Contains(id)) continue;
            batch.Add(fields);
            if (batch.Count >= BatchSize)
            {
                var (ins, skip, err) = await FlushBatchAsync(conn, batch, genreMap, ct);
                inserted += ins; skipped += skip; embedErrors += err;
                batch.Clear();
                Console.Write($"\r  Inseriti: {inserted} | Saltati: {skipped} | Errori: {embedErrors}   ");
            }
        }
        if (batch.Count > 0)
        {
            var (ins, skip, err) = await FlushBatchAsync(conn, batch, genreMap, ct);
            inserted += ins; skipped += skip; embedErrors += err;
        }
        Console.WriteLine();
        return (inserted, skipped, embedErrors);
    }

    private async Task<(int ins, int skip, int err)> FlushBatchAsync(
        NpgsqlConnection conn, List<string[]> batch, Dictionary<string, int> genreMap, CancellationToken ct)
    {
        var texts = batch.Select(BuildEmbeddingText).ToList();
        var embeddings = await _embedder.GenerateEmbeddingsBatchAsync(texts);
        int ins = 0, skip = 0, err = 0;
        for (int i = 0; i < batch.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var embedding = embeddings[i];
            if (embedding == null) { err++; continue; }
            var fields = batch[i];
            if (!int.TryParse(fields[Col.Id], out int tmdbId)) continue;
            long? dbId = await InsertMovieAsync(conn, fields, tmdbId, embedding);
            if (dbId == null) { skip++; continue; }
            var genreIds = ParseGenreNames(fields[Col.Genres])
                .Select(name => genreMap.TryGetValue(name, out int gid) ? gid : -1)
                .Where(gid => gid != -1);
            foreach (var gid in genreIds) await InsertMovieGenreAsync(conn, dbId.Value, gid);
            ins++;
        }
        return (ins, skip, err);
    }

    private static async Task<long?> InsertMovieAsync(NpgsqlConnection conn, string[] f, int tmdbId, float[] embedding)
    {
        DateOnly? releaseDate = null; short? releaseYear = null;
        if (DateOnly.TryParse(f[Col.ReleaseDate], out DateOnly rd)) { releaseDate = rd; releaseYear = (short)rd.Year; }

        static decimal? ParseDecimal(string s) => decimal.TryParse(s.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out decimal v) && v > 0 ? v : null;
        static int? ParseInt(string s) => double.TryParse(s.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) && v > 0 ? (int)Math.Round(v) : null;
        static long? ParseLong(string s) => double.TryParse(s.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) && v > 0 ? (long)v : null;
        static short? ParseShort(string s) => double.TryParse(s.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) && v > 0 ? (short)Math.Round(v) : null;
        static string? S(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        const string Sql = """
            INSERT INTO movies (
                tmdb_id, media_type, title, original_title, overview,
                release_year, release_date, runtime_minutes,
                vote_average, vote_count, popularity, status, original_language,
                is_adult, tagline, poster_path, imdb_id, embedding,
                budget, revenue, cast_list, director, director_of_photography,
                writers, producers, music_composer, imdb_rating, imdb_votes
            )
            VALUES (
                @tmdb_id, 'movie', @title, @original_title, @overview,
                @release_year, @release_date, @runtime,
                @vote_avg, @vote_cnt, @popularity, @status, @orig_lang,
                false, @tagline, @poster, @imdb_id, @embedding,
                @budget, @revenue, @cast, @director, @dop,
                @writers, @producers, @music, @imdb_rating, @imdb_votes
            )
            ON CONFLICT (tmdb_id, media_type) DO NOTHING
            RETURNING id
            """;

        await using var cmd = new NpgsqlCommand(Sql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("tmdb_id", tmdbId));
        cmd.Parameters.Add(new NpgsqlParameter("title", f[Col.Title].Trim()));
        cmd.Parameters.Add(new NpgsqlParameter<string?>("original_title", S(f[Col.OriginalTitle])));
        cmd.Parameters.Add(new NpgsqlParameter<string?>("overview", S(f[Col.Overview])));
        cmd.Parameters.Add(new NpgsqlParameter<short?>("release_year", releaseYear) { NpgsqlDbType = NpgsqlDbType.Smallint });
        cmd.Parameters.Add(new NpgsqlParameter<DateOnly?>("release_date", releaseDate) { NpgsqlDbType = NpgsqlDbType.Date });
        cmd.Parameters.Add(new NpgsqlParameter<short?>("runtime", ParseShort(f[Col.Runtime])) { NpgsqlDbType = NpgsqlDbType.Smallint });
        cmd.Parameters.Add(new NpgsqlParameter<decimal?>("vote_avg", ParseDecimal(f[Col.VoteAverage])) { NpgsqlDbType = NpgsqlDbType.Numeric });
        cmd.Parameters.Add(new NpgsqlParameter<int?>("vote_cnt", ParseInt(f[Col.VoteCount])));
        cmd.Parameters.Add(new NpgsqlParameter<decimal?>("popularity", ParseDecimal(f[Col.Popularity])) { NpgsqlDbType = NpgsqlDbType.Numeric });
        cmd.Parameters.Add(new NpgsqlParameter<string?>("status", S(f[Col.Status])));
        cmd.Parameters.Add(new NpgsqlParameter<string?>("orig_lang", S(f[Col.OriginalLanguage])));
        cmd.Parameters.Add(new NpgsqlParameter<string?>("tagline", S(f[Col.Tagline])));
        cmd.Parameters.Add(new NpgsqlParameter<string?>("poster", S(f[Col.PosterPath])));
        cmd.Parameters.Add(new NpgsqlParameter<string?>("imdb_id", S(f[Col.ImdbId])));
        cmd.Parameters.Add(new NpgsqlParameter("embedding", new Vector(embedding)));
        cmd.Parameters.Add(new NpgsqlParameter<long?>("budget", ParseLong(f[Col.Budget])));
        cmd.Parameters.Add(new NpgsqlParameter<long?>("revenue", ParseLong(f[Col.Revenue])));
        cmd.Parameters.Add(new NpgsqlParameter<string?>("cast", S(f[Col.Cast])));
        cmd.Parameters.Add(new NpgsqlParameter<string?>("director", S(f[Col.Director])));
        cmd.Parameters.Add(new NpgsqlParameter<string?>("dop", S(f[Col.Dop])));
        cmd.Parameters.Add(new NpgsqlParameter<string?>("writers", S(f[Col.Writers])));
        cmd.Parameters.Add(new NpgsqlParameter<string?>("producers", S(f[Col.Producers])));
        cmd.Parameters.Add(new NpgsqlParameter<string?>("music", S(f[Col.MusicComposer])));
        cmd.Parameters.Add(new NpgsqlParameter<decimal?>("imdb_rating", ParseDecimal(f[Col.ImdbRating])) { NpgsqlDbType = NpgsqlDbType.Numeric });
        cmd.Parameters.Add(new NpgsqlParameter<int?>("imdb_votes", ParseInt(f[Col.ImdbVotes])));
        var result = await cmd.ExecuteScalarAsync();
        return result is null or DBNull ? null : Convert.ToInt64(result);
    }

    private static async Task InsertMovieGenreAsync(NpgsqlConnection conn, long movieId, int genreId)
    {
        await using var cmd = new NpgsqlCommand("INSERT INTO movie_genres (movie_id, genre_id) VALUES (@m, @g) ON CONFLICT DO NOTHING", conn);
        cmd.Parameters.Add(new NpgsqlParameter("m", movieId));
        cmd.Parameters.Add(new NpgsqlParameter("g", genreId));
        await cmd.ExecuteNonQueryAsync();
    }

    private static string BuildEmbeddingText(string[] f)
    {
        var parts = new List<string>(3);
        var title = f[Col.Title].Trim();
        if (!string.IsNullOrEmpty(title)) parts.Add(title);
        var overview = f[Col.Overview].Trim();
        if (!string.IsNullOrWhiteSpace(overview)) parts.Add(overview);
        var genres = f[Col.Genres].Trim();
        if (!string.IsNullOrWhiteSpace(genres)) parts.Add("Genres: " + genres);
        return string.Join(". ", parts);
    }

    private static IEnumerable<string> ParseGenreNames(string raw) =>
        string.IsNullOrWhiteSpace(raw) ? [] :
        raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static async IAsyncEnumerable<string[]> ParseCsvAsync(string path, long fileSize)
    {
        await using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, true, 1 << 20);
        var fields = new List<string>(32);
        var field = new StringBuilder(256);
        bool inQuotes = false, isFirst = true;
        long bytesRead = 0, prevReport = 0;
        var buf = new char[1 << 20]; int read;
        while ((read = await reader.ReadAsync(buf, 0, buf.Length)) > 0)
        {
            bytesRead += read;
            for (int i = 0; i < read; i++)
            {
                char c = buf[i];
                if (inQuotes) { if (c == '"') { if (i + 1 < read && buf[i + 1] == '"') { field.Append('"'); i++; } else inQuotes = false; } else field.Append(c); }
                else switch (c)
                {
                    case '"': inQuotes = true; break;
                    case ',': fields.Add(field.ToString()); field.Clear(); break;
                    case '\n':
                        fields.Add(field.ToString()); field.Clear();
                        if (fields.Count > 0) { if (isFirst) { isFirst = false; } else { yield return fields.ToArray(); } fields.Clear(); }
                        break;
                    case '\r': break;
                    default: field.Append(c); break;
                }
            }
            if (bytesRead - prevReport >= 200 * 1024 * 1024) { prevReport = bytesRead; Console.Write($"\r  Lettura CSV: {(double)bytesRead / fileSize * 100:F0}%...   "); }
        }
        if (fields.Count > 0 || field.Length > 0) { fields.Add(field.ToString()); if (!isFirst) yield return fields.ToArray(); }
    }

    public void Dispose() { _embedder.Dispose(); _ds.Dispose(); }
}
