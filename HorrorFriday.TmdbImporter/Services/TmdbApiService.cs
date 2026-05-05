using System.Net;
using System.Text.Json;
using HorrorFriday.TmdbImporter.Models;

namespace HorrorFriday.TmdbImporter.Services;

public sealed class TmdbApiService : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly int _delayMs;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

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

    public async Task<bool> TestConnectionAsync()
    {
        try { return await GetAsync<TmdbPagedResult<TmdbMovieResult>>("discover/movie?page=1") != null; }
        catch { return false; }
    }

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

    public async Task<TmdbMovieDetails?> GetMovieDetailsAsync(int tmdbId)
        => await GetAsync<TmdbMovieDetails>($"movie/{tmdbId}?append_to_response=release_dates,watch%2Fproviders");

    // Full detail including credits, collection, production info — used by Mode [5] dump import
    public async Task<TmdbMovieDetailFull?> GetMovieDetailFullAsync(int tmdbId)
        => await GetAsync<TmdbMovieDetailFull>($"movie/{tmdbId}?append_to_response=release_dates,watch%2Fproviders,credits");

    // Parallel-safe variant: bypasses the sequential gate; caller manages rate limiting
    public async Task<TmdbMovieDetailFull?> GetMovieDetailFullParallelAsync(int tmdbId)
        => await GetAsyncUnthrottled<TmdbMovieDetailFull>($"movie/{tmdbId}?append_to_response=release_dates,watch%2Fproviders,credits");

    private async Task<T?> GetAsyncUnthrottled<T>(string url) where T : class
    {
        int attempt = 0;
        while (true)
        {
            attempt++;
            HttpResponseMessage response;
            try { response = await _http.GetAsync(url); }
            catch (Exception ex) { Console.Error.WriteLine($"  [HTTP error] {url}: {ex.Message}"); return null; }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                int wait = response.Headers.RetryAfter?.Delta.HasValue == true
                    ? (int)response.Headers.RetryAfter!.Delta!.Value.TotalMilliseconds + 200
                    : attempt * 2000;
                Console.WriteLine($"\n  [429] Attendo {wait / 1000.0:F1}s...");
                await Task.Delay(wait);
                continue;
            }
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (!response.IsSuccessStatusCode) { Console.Error.WriteLine($"  [TMDB {(int)response.StatusCode}] {url}"); return null; }

            var stream = await response.Content.ReadAsStreamAsync();
            return await JsonSerializer.DeserializeAsync<T>(stream, _json);
        }
    }

    public async Task<TmdbTvDetails?> GetTvDetailsAsync(int tmdbId)
        => await GetAsync<TmdbTvDetails>($"tv/{tmdbId}?append_to_response=content_ratings,watch%2Fproviders");

    // Full detail including credits — used by Mode [6] TV dump import
    public async Task<TmdbTvDetailFull?> GetTvDetailFullAsync(int tmdbId)
        => await GetAsync<TmdbTvDetailFull>($"tv/{tmdbId}?append_to_response=content_ratings,watch%2Fproviders,credits");

    // Parallel-safe variant: bypasses the sequential gate; caller manages rate limiting
    public async Task<TmdbTvDetailFull?> GetTvDetailFullParallelAsync(int tmdbId)
        => await GetAsyncUnthrottled<TmdbTvDetailFull>($"tv/{tmdbId}?append_to_response=content_ratings,watch%2Fproviders,credits");

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
                try { response = await _http.GetAsync(url); }
                catch (Exception ex) { Console.Error.WriteLine($"  [HTTP error] {url}: {ex.Message}"); return null; }

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    int wait = response.Headers.RetryAfter?.Delta.HasValue == true
                        ? (int)response.Headers.RetryAfter!.Delta!.Value.TotalMilliseconds + 200
                        : attempt * 2000;
                    Console.WriteLine($"\n  [429 Rate limit] Attendo {wait / 1000.0:F1}s...");
                    await Task.Delay(wait);
                    continue;
                }
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                if (!response.IsSuccessStatusCode) { Console.Error.WriteLine($"  [TMDB {(int)response.StatusCode}] {url}"); return null; }

                var stream = await response.Content.ReadAsStreamAsync();
                var result = await JsonSerializer.DeserializeAsync<T>(stream, _json);
                await Task.Delay(_delayMs);
                return result;
            }
        }
        finally { _gate.Release(); }
    }

    public void Dispose() { _http.Dispose(); _gate.Dispose(); }
}
