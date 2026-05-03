using System.Net;
using System.Text.Json;
using HorrorFriday.TmdbSync.Models;

namespace HorrorFriday.TmdbSync.Services;

/// <summary>
/// Thin wrapper around the TMDB v3 API.
/// All calls are serialized through a single semaphore + configurable delay
/// so we stay comfortably below TMDB's 40 req/10 s rate limit.
/// </summary>
public sealed class TmdbApiService : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly int _delayMs;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public TmdbApiService(string apiKey, int delayMs = 250)
    {
        _apiKey = apiKey;
        _delayMs = delayMs;
        _http = new HttpClient
        {
            BaseAddress = new Uri("https://api.themoviedb.org/3/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
        _http.DefaultRequestHeaders.Add("Accept", "application/json");
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    }

    // ─── Connection test ─────────────────────────────────────────────────────

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            var result = await GetAsync<TmdbPagedResult<TmdbMovieResult>>(
                "discover/movie?with_genres=27&page=1");
            return result != null;
        }
        catch
        {
            return false;
        }
    }

    // ─── Discover ────────────────────────────────────────────────────────────

    /// <summary>Discover horror movies, optionally filtering by a release year.</summary>
    public async Task<TmdbPagedResult<TmdbMovieResult>?> DiscoverMoviesAsync(
        string genreParam, int page = 1, int? year = null, DateOnly? releasedAfter = null)
    {
        var url = $"discover/movie?sort_by=release_date.desc&page={page}";
        if (!string.IsNullOrEmpty(genreParam)) url += $"&with_genres={genreParam}";
        if (year.HasValue) url += $"&primary_release_year={year}";
        if (releasedAfter.HasValue) url += $"&primary_release_date.gte={releasedAfter:yyyy-MM-dd}";
        return await GetAsync<TmdbPagedResult<TmdbMovieResult>>(url);
    }

    public async Task<TmdbPagedResult<TmdbTvResult>?> DiscoverTvAsync(
        string genreParam, int page = 1, int? year = null, DateOnly? airedAfter = null)
    {
        var url = $"discover/tv?sort_by=first_air_date.desc&page={page}";
        if (!string.IsNullOrEmpty(genreParam)) url += $"&with_genres={genreParam}";
        if (year.HasValue) url += $"&first_air_date_year={year}";
        if (airedAfter.HasValue) url += $"&first_air_date.gte={airedAfter:yyyy-MM-dd}";
        return await GetAsync<TmdbPagedResult<TmdbTvResult>>(url);
    }

    // ─── Details (with certifications + providers appended) ──────────────────

    /// <summary>
    /// Fetches full movie details including release_dates (certifications) and
    /// watch/providers in a single API call using append_to_response.
    /// </summary>
    public async Task<TmdbMovieDetails?> GetMovieDetailsAsync(int tmdbId)
        => await GetAsync<TmdbMovieDetails>(
            $"movie/{tmdbId}?append_to_response=release_dates,watch%2Fproviders");

    /// <summary>
    /// Fetches full TV show details including content_ratings (certifications)
    /// and watch/providers in a single API call.
    /// </summary>
    public async Task<TmdbTvDetails?> GetTvDetailsAsync(int tmdbId)
        => await GetAsync<TmdbTvDetails>(
            $"tv/{tmdbId}?append_to_response=content_ratings,watch%2Fproviders");

    // ─── Core HTTP ───────────────────────────────────────────────────────────

    private async Task<T?> GetAsync<T>(string url) where T : class
    {
        await _gate.WaitAsync();
        try
        {
            int attempt = 0;
            while (true)
            {
                attempt++;
                HttpResponseMessage response;
                try
                {
                    response = await _http.GetAsync(url);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  [HTTP error] {url}: {ex.Message}");
                    return null;
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    // Honour Retry-After if present, otherwise back off exponentially
                    int wait = response.Headers.RetryAfter?.Delta.HasValue == true
                        ? (int)response.Headers.RetryAfter!.Delta!.Value.TotalMilliseconds + 200
                        : attempt * 2000;

                    Console.WriteLine($"\n  [429 Rate limit] Waiting {wait / 1000.0:F1}s before retry...");
                    await Task.Delay(wait);
                    continue;
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                    return null;

                if (!response.IsSuccessStatusCode)
                {
                    Console.Error.WriteLine($"  [TMDB {(int)response.StatusCode}] {url}");
                    return null;
                }

                var stream = await response.Content.ReadAsStreamAsync();
                var result = await JsonSerializer.DeserializeAsync<T>(stream, _json);

                // Enforce the configured delay between requests
                await Task.Delay(_delayMs);
                return result;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }
}
