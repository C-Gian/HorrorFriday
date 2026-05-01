using System.Net.Http.Json;
using System.Text.Json;

namespace HorrorFriday.API.Services;

public class EmbeddingService : IDisposable
{
    private readonly HttpClient _httpClient;
    private const string OPENAI_URL = "https://api.openai.com/v1/embeddings";
    private const string MODEL = "text-embedding-3-small";

    public EmbeddingService(string apiKey)
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<float[]?> GenerateEmbeddingAsync(string text)
    {
        if (text.Length > 8000)
            text = text[..8000];

        var requestBody = new { model = MODEL, input = text };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(OPENAI_URL, requestBody);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[EmbeddingService] OpenAI error ({response.StatusCode}): {error}");
                return null;
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var embeddingArray = json.GetProperty("data")[0].GetProperty("embedding");

            var embedding = new float[embeddingArray.GetArrayLength()];
            for (int i = 0; i < embedding.Length; i++)
                embedding[i] = embeddingArray[i].GetSingle();

            return embedding;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EmbeddingService] Error: {ex.Message}");
            return null;
        }
    }

    public void Dispose() => _httpClient.Dispose();
}
