using System.Net.Http.Json;
using System.Text.Json;

namespace HorrorFriday.CsvImporter.Services;

public class EmbeddingService : IDisposable
{
    private readonly HttpClient _httpClient;
    private const string OPENAI_URL = "https://api.openai.com/v1/embeddings";
    private const string MODEL = "text-embedding-3-small";

    public EmbeddingService(string apiKey)
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
    }

    public async Task<float[]?> GenerateEmbeddingAsync(string text)
    {
        if (text.Length > 8000) text = text[..8000];
        try
        {
            var response = await _httpClient.PostAsJsonAsync(OPENAI_URL, new { model = MODEL, input = text });
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"  OpenAI API error ({response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
                return null;
            }
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var arr = json.GetProperty("data")[0].GetProperty("embedding");
            var emb = new float[arr.GetArrayLength()];
            for (int i = 0; i < emb.Length; i++) emb[i] = arr[i].GetSingle();
            return emb;
        }
        catch (Exception ex) { Console.WriteLine($"  OpenAI API error: {ex.Message}"); return null; }
    }

    public async Task<List<float[]?>> GenerateEmbeddingsBatchAsync(List<string> texts)
    {
        var truncated = texts.Select(t => t.Length > 8000 ? t[..8000] : t).ToList();
        try
        {
            var response = await _httpClient.PostAsJsonAsync(OPENAI_URL, new { model = MODEL, input = truncated });
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"  OpenAI API batch error ({response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
                return texts.Select(_ => (float[]?)null).ToList();
            }
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var results = new List<float[]?>();
            foreach (var item in json.GetProperty("data").EnumerateArray())
            {
                var arr = item.GetProperty("embedding");
                var emb = new float[arr.GetArrayLength()];
                for (int i = 0; i < emb.Length; i++) emb[i] = arr[i].GetSingle();
                results.Add(emb);
            }
            return results;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  OpenAI API batch error: {ex.Message}");
            return texts.Select(_ => (float[]?)null).ToList();
        }
    }

    public void Dispose() => _httpClient.Dispose();
}
