using System.Diagnostics;
using System.Text;
using Npgsql;

namespace HorrorFriday.TmdbSync.Services;

/// <summary>
/// Enriches existing movie records with the new columns present only in the
/// new Kaggle CSV (cast, director, crew, budget, revenue, IMDb rating/votes).
///
/// Strategy:
///   1. Stream the new CSV and write matching rows to an in-session PostgreSQL
///      TEMP TABLE via COPY FROM STDIN (fastest bulk path, no file on disk).
///   2. A single UPDATE … FROM staging patches every matched row at once.
///   3. Drop the temp table.
///
/// This never deletes or re-inserts rows — it is purely additive.
/// </summary>
public sealed class CsvEnrichmentService
{
    private readonly NpgsqlDataSource _ds;

    // Column indices in the new CSV (0-based, verified against header)
    private static class Col
    {
        public const int Id = 0;
        public const int Revenue = 6;
        public const int Budget = 8;
        public const int Cast = 19;
        public const int Director = 20;
        public const int Dop = 21;
        public const int Writers = 22;
        public const int Producers = 23;
        public const int MusicComposer = 24;
        public const int ImdbRating = 25;
        public const int ImdbVotes = 26;
    }

    public CsvEnrichmentService(string connectionString)
    {
        _ds = NpgsqlDataSource.Create(connectionString);
    }

    public async Task RunAsync(string newCsvPath)
    {
        if (!File.Exists(newCsvPath))
            throw new FileNotFoundException($"Nuovo CSV non trovato: {newCsvPath}");

        var sw = Stopwatch.StartNew();

        // ── Step 1: Load existing tmdb_ids from DB ────────────────────────────
        Console.WriteLine("Caricamento ID dal database...");
        var dbIds = await LoadDbIdsAsync();
        Console.WriteLine($"  {dbIds.Count:N0} record nel DB.\n");

        // ── Step 2: Create staging table ──────────────────────────────────────
        Console.WriteLine("Creazione tabella di staging temporanea...");
        await using var conn = await _ds.OpenConnectionAsync();
        await CreateStagingTableAsync(conn);

        // ── Step 3: Stream new CSV → COPY into staging ────────────────────────
        Console.WriteLine("Lettura nuovo CSV e caricamento in staging (COPY FROM STDIN)...");
        int staged = await CopyMatchingRowsAsync(conn, newCsvPath, dbIds);
        Console.WriteLine($"\n  {staged:N0} righe caricate in staging ({sw.Elapsed.ToString(@"mm\:ss")}).\n");

        if (staged == 0)
        {
            Console.WriteLine("  Nessuna riga corrispondente trovata. Enrichment non necessario.");
            return;
        }

        // ── Step 4: Bulk UPDATE from staging ──────────────────────────────────
        Console.WriteLine("Esecuzione UPDATE bulk dal DB...");
        int updated = await ApplyEnrichmentAsync(conn);
        Console.WriteLine($"  {updated:N0} record aggiornati.\n");

        // ── Step 5: Drop staging ──────────────────────────────────────────────
        await using var drop = new NpgsqlCommand("DROP TABLE IF EXISTS enrichment_staging", conn);
        await drop.ExecuteNonQueryAsync();

        sw.Stop();
        // Compute elapsed separately — raw string literals pass backslashes literally
        // to format specifiers, which breaks TimeSpan.ToString with \\: sequences.
        var elapsedStr = sw.Elapsed.ToString(@"mm\:ss");
        int unchanged = dbIds.Count - updated;
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"""

  ┌─ Enrichment completato ────────────────────────────────────┐
  │  Righe nel nuovo CSV corrispondenti al DB : {staged,8:N0}        │
  │  Record aggiornati nel DB                 : {updated,8:N0}        │
  │  Record non nel nuovo CSV (invariati)     : {unchanged,8:N0}        │
  │  Tempo totale                             : {elapsedStr}                │
  └────────────────────────────────────────────────────────────┘
""");
        Console.ResetColor();
    }

    // ─── DB helpers ──────────────────────────────────────────────────────────

    private async Task<HashSet<int>> LoadDbIdsAsync()
    {
        var ids = new HashSet<int>(350_000);
        await using var conn = await _ds.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT tmdb_id FROM movies WHERE media_type = 'movie'", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) ids.Add(r.GetInt32(0));
        return ids;
    }

    private static async Task CreateStagingTableAsync(NpgsqlConnection conn)
    {
        await using var cmd = new NpgsqlCommand("""
            DROP TABLE IF EXISTS enrichment_staging;
            CREATE TEMP TABLE enrichment_staging (
                tmdb_id              INTEGER NOT NULL,
                budget               BIGINT,
                revenue              BIGINT,
                cast_list            TEXT,
                director             TEXT,
                director_of_photography TEXT,
                writers              TEXT,
                producers            TEXT,
                music_composer       TEXT,
                imdb_rating          NUMERIC(4,2),
                imdb_votes           INTEGER
            );
            """, conn);
        cmd.CommandTimeout = 60;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<int> CopyMatchingRowsAsync(
        NpgsqlConnection conn, string csvPath, HashSet<int> dbIds)
    {
        int count = 0;
        long fileSize = new FileInfo(csvPath).Length;

        await using var writer = await conn.BeginTextImportAsync(
            "COPY enrichment_staging " +
            "(tmdb_id, budget, revenue, cast_list, director, director_of_photography, " +
            "writers, producers, music_composer, imdb_rating, imdb_votes) " +
            "FROM STDIN");

        await foreach (var fields in ParseCsvAsync(csvPath, fileSize, skipHeader: true))
        {
            if (fields.Length <= Col.ImdbVotes) continue;

            if (!int.TryParse(fields[Col.Id], out int tmdbId)) continue;
            if (!dbIds.Contains(tmdbId)) continue;

            // Build the TSV line for COPY (tab-separated, \N for NULL)
            var sb = new StringBuilder(256);
            sb.Append(tmdbId); sb.Append('\t');
            AppendLong(sb, fields[Col.Budget]); sb.Append('\t');
            AppendLong(sb, fields[Col.Revenue]); sb.Append('\t');
            AppendText(sb, fields[Col.Cast]); sb.Append('\t');
            AppendText(sb, fields[Col.Director]); sb.Append('\t');
            AppendText(sb, fields[Col.Dop]); sb.Append('\t');
            AppendText(sb, fields[Col.Writers]); sb.Append('\t');
            AppendText(sb, fields[Col.Producers]); sb.Append('\t');
            AppendText(sb, fields[Col.MusicComposer]); sb.Append('\t');
            AppendDecimal(sb, fields[Col.ImdbRating]); sb.Append('\t');
            AppendInt(sb, fields[Col.ImdbVotes]);

            await writer.WriteLineAsync(sb.ToString());
            count++;

            if (count % 10_000 == 0)
                Console.Write($"\r  Staging: {count:N0} righe...");
        }

        return count;
    }

    private static async Task<int> ApplyEnrichmentAsync(NpgsqlConnection conn)
    {
        await using var cmd = new NpgsqlCommand("""
            UPDATE movies m SET
                budget                  = s.budget,
                revenue                 = s.revenue,
                cast_list               = s.cast_list,
                director                = s.director,
                director_of_photography = s.director_of_photography,
                writers                 = s.writers,
                producers               = s.producers,
                music_composer          = s.music_composer,
                imdb_rating             = s.imdb_rating,
                imdb_votes              = s.imdb_votes
            FROM enrichment_staging s
            WHERE m.tmdb_id = s.tmdb_id
              AND m.media_type = 'movie'
            """, conn);
        cmd.CommandTimeout = 600; // large table, allow up to 10 min
        return await cmd.ExecuteNonQueryAsync();
    }

    // ─── CSV helpers ─────────────────────────────────────────────────────────

    // Yields complete records (string[]) from a CSV, handling quoted multiline fields.
    private static async IAsyncEnumerable<string[]> ParseCsvAsync(
        string path, long fileSize, bool skipHeader)
    {
        await using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 1 << 20);

        var fields = new List<string>(32);
        var field = new StringBuilder(256);
        bool inQuotes = false;
        bool isFirst = true;
        long bytesRead = 0;
        long prevReport = 0;

        var buf = new char[1 << 20]; // 1 MB
        int read;

        while ((read = await reader.ReadAsync(buf, 0, buf.Length)) > 0)
        {
            bytesRead += read;

            for (int i = 0; i < read; i++)
            {
                char c = buf[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        // Escaped quote: "" → "
                        if (i + 1 < read && buf[i + 1] == '"') { field.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else field.Append(c);
                }
                else
                {
                    switch (c)
                    {
                        case '"':
                            inQuotes = true;
                            break;
                        case ',':
                            fields.Add(field.ToString());
                            field.Clear();
                            break;
                        case '\n':
                            fields.Add(field.ToString());
                            field.Clear();
                            if (fields.Count > 0)
                            {
                                if (isFirst && skipHeader) { isFirst = false; }
                                else { yield return fields.ToArray(); }
                                fields.Clear();
                            }
                            break;
                        case '\r':
                            break;
                        default:
                            field.Append(c);
                            break;
                    }
                }
            }

            if (bytesRead - prevReport >= 100 * 1024 * 1024)
            {
                prevReport = bytesRead;
                double pct = (double)bytesRead / fileSize * 100;
                Console.Write($"\r  Lettura CSV: {pct:F0}%...");
            }
        }

        // Last record if no trailing newline
        if (fields.Count > 0 || field.Length > 0)
        {
            fields.Add(field.ToString());
            if (!isFirst) yield return fields.ToArray();
        }
    }

    // ─── COPY field formatters ────────────────────────────────────────────────

    // PostgreSQL TEXT COPY: NULL = \N, tab/newline/backslash must be escaped.
    private static void AppendText(StringBuilder sb, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) { sb.Append("\\N"); return; }
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '\t': sb.Append("\\t");  break;
                case '\n': sb.Append("\\n");  break;
                case '\r': sb.Append("\\r");  break;
                default:   sb.Append(c);      break;
            }
        }
    }

    // Values arrive as "160000000.0" (float notation) — parse via double to avoid
    // TrimEnd bugs that strip meaningful trailing digits along with the decimal part.
    private static void AppendLong(StringBuilder sb, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) { sb.Append("\\N"); return; }
        if (double.TryParse(value.Trim(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double d) && d > 0)
            sb.Append((long)d);
        else
            sb.Append("\\N");
    }

    // "371.0" → 371
    private static void AppendInt(StringBuilder sb, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) { sb.Append("\\N"); return; }
        if (double.TryParse(value.Trim(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double d) && d > 0)
            sb.Append((long)Math.Round(d));
        else
            sb.Append("\\N");
    }

    // "7.106" → "7.11"
    private static void AppendDecimal(StringBuilder sb, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) { sb.Append("\\N"); return; }
        if (decimal.TryParse(value.Trim(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out decimal d) && d > 0)
            sb.Append(d.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
        else
            sb.Append("\\N");
    }
}
