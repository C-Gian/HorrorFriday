namespace HorrorFriday.TmdbSync.Services;

/// <summary>
/// Reads two TMDB CSV files and computes which TMDB IDs exist in one but not the other.
/// Uses a hand-written streaming parser so it can handle 1M+ row files without loading
/// them into memory and without any external CSV library.
/// </summary>
public sealed class CsvAnalysisService
{
    /// <summary>
    /// Compares IDs between the old and the new CSV.
    /// Saves two text files (one ID per line) in <paramref name="outputDir"/>.
    /// </summary>
    public async Task RunDiffAsync(string oldCsvPath, string newCsvPath, string outputDir)
    {
        Validate(oldCsvPath, "Old CSV");
        Validate(newCsvPath, "New CSV");
        Directory.CreateDirectory(outputDir);

        Console.WriteLine();
        var oldIds = await ReadIdsAsync(oldCsvPath, "vecchio CSV");
        var newIds = await ReadIdsAsync(newCsvPath, "nuovo CSV");

        Console.WriteLine("\nCalcolo differenze...");

        var onlyInOld = new List<int>(capacity: 200_000);
        var onlyInNew = new List<int>(capacity: 200_000);

        foreach (var id in oldIds)
            if (!newIds.Contains(id)) onlyInOld.Add(id);

        foreach (var id in newIds)
            if (!oldIds.Contains(id)) onlyInNew.Add(id);

        onlyInOld.Sort();
        onlyInNew.Sort();

        int inBoth = oldIds.Count + newIds.Count - onlyInOld.Count - onlyInNew.Count;

        // Write output files
        var oldOnlyPath = Path.Combine(outputDir, "diff_only_in_old.txt");
        var newOnlyPath = Path.Combine(outputDir, "diff_only_in_new.txt");

        await File.WriteAllLinesAsync(oldOnlyPath, onlyInOld.Select(x => x.ToString()));
        await File.WriteAllLinesAsync(newOnlyPath, onlyInNew.Select(x => x.ToString()));

        // Summary
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"""

  ┌─ Risultato Diff ──────────────────────────────────────────────────────┐
  │  ID nel vecchio CSV         : {oldIds.Count,10:N0}                          │
  │  ID nel nuovo CSV           : {newIds.Count,10:N0}                          │
  │                                                                       │
  │  ID presenti in ENTRAMBI    : {inBoth,10:N0}                          │
  │  Solo nel VECCHIO (mancano nel nuovo): {onlyInOld.Count,6:N0}                │
  │  Solo nel NUOVO  (non nel vecchio)  : {onlyInNew.Count,6:N0}                │
  └───────────────────────────────────────────────────────────────────────┘

  File salvati in: {outputDir}
    → {Path.GetFileName(oldOnlyPath)}   ({onlyInOld.Count:N0} righe)
    → {Path.GetFileName(newOnlyPath)}   ({onlyInNew.Count:N0} righe)
""");
        Console.ResetColor();
    }

    // ─── Fast streaming CSV ID reader ────────────────────────────────────────

    /// <summary>
    /// Reads only the first column (id) from a TMDB CSV.
    /// Handles RFC-4180 quoting and multiline fields correctly using a state machine.
    /// Uses a 1 MB read buffer for performance.
    /// </summary>
    private static async Task<HashSet<int>> ReadIdsAsync(string path, string label)
    {
        var ids = new HashSet<int>(1_600_000);
        long fileSize = new FileInfo(path).Length;

        await using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true, bufferSize: 1 << 20);

        // Tiny fixed-size field buffer — TMDB IDs fit in 8 chars
        const int MAX_FIELD = 12;
        var fieldBuf = new char[MAX_FIELD];
        int fieldLen = 0;

        bool inQuotes = false;
        bool onFirstField = true;
        bool headerSkipped = false;

        // Read in 1 MB chunks
        var buf = new char[1 << 20];
        int read;
        long totalRead = 0;
        long prevReport = 0;

        while ((read = await reader.ReadAsync(buf, 0, buf.Length)) > 0)
        {
            totalRead += read;

            for (int i = 0; i < read; i++)
            {
                char c = buf[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        // Peek for escaped-quote ("") — peek into same buffer if possible,
                        // otherwise skip (edge case at buffer boundary is harmless for int fields)
                        bool escaped = (i + 1 < read && buf[i + 1] == '"');
                        if (escaped)
                        {
                            i++; // consume the second quote
                            // escaped quotes never appear in integer IDs, so don't append
                        }
                        else
                        {
                            inQuotes = false; // closing quote
                        }
                    }
                    else if (onFirstField && fieldLen < MAX_FIELD)
                    {
                        fieldBuf[fieldLen++] = c;
                    }
                }
                else
                {
                    switch (c)
                    {
                        case '"':
                            inQuotes = true;
                            break;

                        case ',':
                            if (onFirstField)
                            {
                                // End of first field → try to parse the ID
                                if (headerSkipped && fieldLen > 0 &&
                                    int.TryParse(fieldBuf.AsSpan(0, fieldLen), out int id))
                                {
                                    ids.Add(id);
                                }
                                fieldLen = 0;
                                onFirstField = false;
                            }
                            break;

                        case '\n':
                            // End of record (unquoted newline = record boundary)
                            if (!headerSkipped)
                                headerSkipped = true;
                            onFirstField = true;
                            fieldLen = 0;
                            break;

                        case '\r':
                            break; // skip CR in CRLF line endings

                        default:
                            if (onFirstField && fieldLen < MAX_FIELD)
                                fieldBuf[fieldLen++] = c;
                            break;
                    }
                }
            }

            // Progress every ~50 MB
            if (totalRead - prevReport >= 50 * 1024 * 1024)
            {
                prevReport = totalRead;
                double pct = (double)totalRead / fileSize * 100;
                Console.Write($"\r  Lettura {label}: {pct:F0}% ({ids.Count:N0} ID letti)...   ");
            }
        }

        Console.Write($"\r  Lettura {label}: 100% — {ids.Count:N0} ID totali.          \n");
        return ids;
    }

    private static void Validate(string path, string label)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"{label} non trovato: {path}");
    }
}
