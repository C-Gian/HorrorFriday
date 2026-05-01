using System.Net.Http.Json;
using System.Text.Json;

namespace HorrorFriday.Importer.Services;

/// <summary>
/// Calls the OpenAI API to generate embeddings (arrays of numbers that
/// represent the "meaning" of a text). Uses the text-embedding-3-small model.
/// </summary>
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

    /// <summary>
    /// Generates an embedding for a single text string.
    /// Returns an array of 1536 floats, or null if the request fails.
    /// </summary>
    public async Task<float[]?> GenerateEmbeddingAsync(string text)
    {
        // OpenAI has a token limit per request. Truncate very long texts
        // to avoid errors. ~8000 chars is roughly 2000 tokens, well within limits.
        if (text.Length > 8000)
            text = text[..8000];

        var requestBody = new
        {
            model = MODEL,
            input = text
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(OPENAI_URL, requestBody);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"  OpenAI API error ({response.StatusCode}): {errorBody}");
                return null;
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            // The response structure is:
            // { "data": [ { "embedding": [0.123, -0.456, ...] } ] }
            var embeddingArray = json
                .GetProperty("data")[0]
                .GetProperty("embedding");

            var embedding = new float[embeddingArray.GetArrayLength()];
            for (int i = 0; i < embedding.Length; i++)
            {
                embedding[i] = embeddingArray[i].GetSingle();
            }

            return embedding;
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine("  OpenAI API timeout - will retry");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  OpenAI API error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Generates embeddings for multiple texts in a single API call (batch).
    /// OpenAI supports up to 2048 texts per batch request.
    /// This is much faster and cheaper than calling one by one.
    /// Returns a list of embeddings in the same order as the input texts.
    /// </summary>
    public async Task<List<float[]?>> GenerateEmbeddingsBatchAsync(List<string> texts)
    {
        // Truncate long texts
        var truncated = texts.Select(t => t.Length > 8000 ? t[..8000] : t).ToList();

        var requestBody = new
        {
            model = MODEL,
            input = truncated
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(OPENAI_URL, requestBody);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"  OpenAI API batch error ({response.StatusCode}): {errorBody}");
                // Return nulls for all texts in the batch
                return texts.Select(_ => (float[]?)null).ToList();
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var dataArray = json.GetProperty("data");

            var results = new List<float[]?>();

            foreach (var item in dataArray.EnumerateArray())
            {
                var embeddingArray = item.GetProperty("embedding");
                var embedding = new float[embeddingArray.GetArrayLength()];
                for (int i = 0; i < embedding.Length; i++)
                {
                    embedding[i] = embeddingArray[i].GetSingle();
                }
                results.Add(embedding);
            }

            return results;
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine("  OpenAI API batch timeout - will retry");
            return texts.Select(_ => (float[]?)null).ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  OpenAI API batch error: {ex.Message}");
            return texts.Select(_ => (float[]?)null).ToList();
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}