namespace HorrorFriday.CsvImporter.Services;

public sealed class CsvAnalysisService
{
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

        foreach (var id in oldIds) if (!newIds.Contains(id)) onlyInOld.Add(id);
        foreach (var id in newIds) if (!oldIds.Contains(id)) onlyInNew.Add(id);

        onlyInOld.Sort();
        onlyInNew.Sort();

        int inBoth = oldIds.Count + newIds.Count - onlyInOld.Count - onlyInNew.Count;

        var oldOnlyPath = Path.Combine(outputDir, "diff_only_in_old.txt");
        var newOnlyPath = Path.Combine(outputDir, "diff_only_in_new.txt");

        await File.WriteAllLinesAsync(oldOnlyPath, onlyInOld.Select(x => x.ToString()));
        await File.WriteAllLinesAsync(newOnlyPath, onlyInNew.Select(x => x.ToString()));

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

    private static async Task<HashSet<int>> ReadIdsAsync(string path, string label)
    {
        var ids = new HashSet<int>(1_600_000);
        long fileSize = new FileInfo(path).Length;
        await using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, true, 1 << 20);

        const int MAX_FIELD = 12;
        var fieldBuf = new char[MAX_FIELD];
        int fieldLen = 0;
        bool inQuotes = false, onFirstField = true, headerSkipped = false;
        var buf = new char[1 << 20];
        int read;
        long totalRead = 0, prevReport = 0;

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
                        if (i + 1 < read && buf[i + 1] == '"') i++;
                        else inQuotes = false;
                    }
                    else if (onFirstField && fieldLen < MAX_FIELD) fieldBuf[fieldLen++] = c;
                }
                else switch (c)
                {
                    case '"': inQuotes = true; break;
                    case ',':
                        if (onFirstField)
                        {
                            if (headerSkipped && fieldLen > 0 && int.TryParse(fieldBuf.AsSpan(0, fieldLen), out int id))
                                ids.Add(id);
                            fieldLen = 0; onFirstField = false;
                        }
                        break;
                    case '\n':
                        if (!headerSkipped) headerSkipped = true;
                        onFirstField = true; fieldLen = 0; break;
                    case '\r': break;
                    default:
                        if (onFirstField && fieldLen < MAX_FIELD) fieldBuf[fieldLen++] = c; break;
                }
            }
            if (totalRead - prevReport >= 50 * 1024 * 1024)
            {
                prevReport = totalRead;
                Console.Write($"\r  Lettura {label}: {(double)totalRead / fileSize * 100:F0}% ({ids.Count:N0} ID)...   ");
            }
        }
        Console.Write($"\r  Lettura {label}: 100% — {ids.Count:N0} ID totali.          \n");
        return ids;
    }

    private static void Validate(string path, string label)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"{label} non trovato: {path}");
    }
}
