using System.Net.Http.Json;
using System.Text.Json;

namespace HorrorFriday.TmdbSync.Services;

public sealed class EmbeddingService : IDisposable
{
    private readonly HttpClient _http;
    private const string Url = "https://api.openai.com/v1/embeddings";
    private const string Model = "text-embedding-3-small";

    public EmbeddingService(string apiKey)
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        _http.Timeout = TimeSpan.FromSeconds(60);
    }

    public async Task<List<float[]?>> GenerateBatchAsync(List<string> texts)
    {
        var truncated = texts.Select(t => t.Length > 8000 ? t[..8000] : t).ToList();

        try
        {
            var response = await _http.PostAsJsonAsync(Url, new { model = Model, input = truncated });

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"\n  OpenAI error ({response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
                return texts.Select(_ => (float[]?)null).ToList();
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var data = json.GetProperty("data");

            var results = new List<float[]?>();
            foreach (var item in data.EnumerateArray())
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
            Console.WriteLine($"\n  OpenAI batch error: {ex.Message}");
            return texts.Select(_ => (float[]?)null).ToList();
        }
    }

    public void Dispose() => _http.Dispose();
}
