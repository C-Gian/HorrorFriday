using System.Text.Json;
using Npgsql;
using HorrorFriday.TmdbImporter.Models;

namespace HorrorFriday.TmdbImporter.Services;

public sealed class EmbeddingBackfillService : IDisposable
{
    private readonly NpgsqlDataSource _ds;
    private readonly HttpClient _http;
    private readonly int _batchSize;

    public EmbeddingBackfillService(string connectionString, string openAiKey, int batchSize)
    {
        _ds = NpgsqlDataSource.Create(connectionString);
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", openAiKey);
        _batchSize = batchSize;
    }

    public async Task<SyncStats> RunAsync(CancellationToken ct)
    {
        var stats = new SyncStats();
        var total = await CountNullEmbeddingsAsync();
        Console.WriteLine($"  Record senza embedding: {total:N0}");

        if (total == 0)
        {
            Console.WriteLine("  Nessun record da processare.\n");
            return stats;
        }

        long processed = 0;

        while (!ct.IsCancellationRequested)
        {
            var batch = await GetNextBatchAsync(_batchSize);
            if (batch.Count == 0) break;

            var texts = batch.Select(r => BuildText(r.Title, r.Overview)).ToList();

            float[][]? embeddings;
            try
            {
                embeddings = await GetEmbeddingsAsync(texts, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  OpenAI error: {ex.Message} — retry in 5s...");
                Console.ResetColor();
                stats.Errors++;
                try { await Task.Delay(5000, ct); } catch (OperationCanceledException) { break; }
                continue;
            }

            try
            {
                await UpdateEmbeddingsBatchAsync(batch.Select(r => r.Id).ToList(), embeddings);
                stats.Enriched += batch.Count;
                processed += batch.Count;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  DB batch error: {ex.Message}");
                Console.ResetColor();
                stats.Errors += batch.Count;
            }

            var pct = total > 0 ? (double)processed / total * 100 : 0;
            Console.WriteLine($"  {processed:N0} / {total:N0} ({pct:F1}%) completati");

            try { await Task.Delay(150, ct); } catch (OperationCanceledException) { break; }
        }

        if (ct.IsCancellationRequested)
            Console.WriteLine("\n  Backfill interrotto. Riprendi con [7] — i record già processati sono salvati.");
        else
            Console.WriteLine("\n  Backfill completato!");

        return stats;
    }

    private async Task<long> CountNullEmbeddingsAsync()
    {
        await using var conn = await _ds.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM movies WHERE embedding IS NULL", conn);
        return (long)(await cmd.ExecuteScalarAsync() ?? 0L);
    }

    private async Task<List<(int Id, string? Title, string? Overview)>> GetNextBatchAsync(int limit)
    {
        var list = new List<(int, string?, string?)>();
        await using var conn = await _ds.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT id, title, overview FROM movies WHERE embedding IS NULL ORDER BY id LIMIT $1", conn);
        cmd.Parameters.AddWithValue(limit);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add((
                reader.GetInt32(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)
            ));
        return list;
    }

    private static string BuildText(string? title, string? overview)
    {
        var t = string.IsNullOrWhiteSpace(title) ? "" : title.Trim();
        var o = string.IsNullOrWhiteSpace(overview) ? "" : overview.Trim();
        return string.IsNullOrEmpty(o) ? t : $"{t}: {o}";
    }

    private async Task<float[][]> GetEmbeddingsAsync(List<string> texts, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new { model = "text-embedding-3-small", input = texts });
        using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("https://api.openai.com/v1/embeddings", content, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);

        var data = doc.RootElement.GetProperty("data");
        var results = new float[data.GetArrayLength()][];

        foreach (var item in data.EnumerateArray())
        {
            var idx = item.GetProperty("index").GetInt32();
            var arr = item.GetProperty("embedding");
            var vec = new float[arr.GetArrayLength()];
            int j = 0;
            foreach (var val in arr.EnumerateArray())
                vec[j++] = val.GetSingle();
            results[idx] = vec;
        }

        return results;
    }

    private async Task UpdateEmbeddingsBatchAsync(List<int> ids, float[][] embeddings)
    {
        await using var conn = await _ds.OpenConnectionAsync();
        for (int i = 0; i < ids.Count; i++)
        {
            var vectorStr = "[" + string.Join(",",
                embeddings[i].Select(f => f.ToString(System.Globalization.CultureInfo.InvariantCulture))) + "]";
            await using var cmd = new NpgsqlCommand(
                "UPDATE movies SET embedding = $1::vector WHERE id = $2", conn);
            cmd.Parameters.AddWithValue(vectorStr);
            cmd.Parameters.AddWithValue(ids[i]);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        _ds.Dispose();
    }
}
