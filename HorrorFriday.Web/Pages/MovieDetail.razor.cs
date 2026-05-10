using System.Net;
using System.Net.Http.Json;
using HorrorFriday.Web.Models;
using HorrorFriday.Web.Services;
using Microsoft.AspNetCore.Components;

namespace HorrorFriday.Web.Pages;

public partial class MovieDetail : ComponentBase
{
    [Parameter]
    public int MovieId { get; set; }

    [Inject] private HttpClient Http { get; set; } = default!;
    [Inject] protected AuthService AuthService { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    protected MovieDto? Movie { get; set; }
    protected List<MovieDto> SimilarMovies { get; set; } = new();
    protected bool IsLoading { get; set; } = true;
    protected string ActiveTab { get; set; } = "cast";
    protected bool IsSaving { get; set; }
    protected bool HasUserEntry { get; set; }
    protected bool IsListMenuOpen { get; set; }
    protected string SelectedStatus { get; set; } = "to_watch";
    protected int? SelectedRating { get; set; }
    protected string UserNotes { get; set; } = "";
    protected string? SaveMessage { get; set; }

    protected static readonly List<(string Value, string Label)> StatusOptions = new()
    {
        ("to_watch", "Da guardare"),
        ("watching", "In visione"),
        ("watched", "Visto"),
        ("dropped", "Abbandonato")
    };

    protected sealed record GenreTileModel(string Name, string Color, string IconUrl, string BackgroundImageUrl);

    private static readonly IReadOnlyDictionary<string, GenreTileModel> GenreTiles =
        new Dictionary<string, GenreTileModel>(StringComparer.OrdinalIgnoreCase)
        {
            ["Action"] = new("Action", "#ff941f", "/images/genres/crosshair.svg", "/images/genres/Action.png"),
            ["Science Fiction"] = new("Science Fiction", "#16d9e3", "/images/genres/rocket.svg", "/images/genres/ScienceFiction.png"),
            ["Adventure"] = new("Adventure", "#42c46d", "/images/genres/compass.svg", "/images/genres/Adventure.png"),
            ["Drama"] = new("Drama", "#b690ff", "/images/genres/drama.svg", "/images/genres/Drama.png"),
            ["Crime"] = new("Crime", "#9aa5ad", "/images/genres/fingerprint-pattern.svg", "/images/genres/Crime.png"),
            ["Thriller"] = new("Thriller", "#b372ff", "/images/genres/eye.svg", "/images/genres/Thriller.png"),
            ["Fantasy"] = new("Fantasy", "#a66cff", "/images/genres/sparkles.svg", "/images/genres/Fantasy.png"),
            ["Comedy"] = new("Comedy", "#ffc02e", "/images/genres/theater.svg", "/images/genres/Comedy.png"),
            ["Romance"] = new("Romance", "#ff5f93", "/images/genres/heart.svg", "/images/genres/Romance.png"),
            ["Western"] = new("Western", "#d08b45", "/images/genres/hat-glasses.svg", "/images/genres/Western.png"),
            ["Mystery"] = new("Mystery", "#a675ff", "/images/genres/scan-search.svg", "/images/genres/Mystery.png"),
            ["War"] = new("War", "#9a8d6b", "/images/genres/shield.svg", "/images/genres/War.png"),
            ["Animation"] = new("Animation", "#6ab7ff", "/images/genres/clapperboard.svg", "/images/genres/Animation.png"),
            ["Family"] = new("Family", "#7ddf64", "/images/genres/users.svg", "/images/genres/Family.png"),
            ["Horror"] = new("Horror", "#ff3e3e", "/images/genres/skull.svg", "/images/genres/Horror.png"),
            ["Music"] = new("Music", "#f062b8", "/images/genres/music.svg", "/images/genres/Music.png"),
            ["History"] = new("History", "#c89b63", "/images/genres/landmark.svg", "/images/genres/History.png"),
            ["TV Movie"] = new("TV Movie", "#16d9e3", "/images/genres/tv.svg", "/images/genres/TvMovie.png"),
            ["Documentary"] = new("Documentary", "#70d6bd", "/images/genres/book-search.svg", "/images/genres/Documentary.png"),
        };

    protected override async Task OnParametersSetAsync()
    {
        IsLoading = true;
        SaveMessage = null;

        await AuthService.InitializeAsync();
        await LoadMovieAsync();

        if (AuthService.IsLoggedIn)
            await LoadUserEntryAsync();

        await LoadSimilarAsync();

        IsLoading = false;
    }

    private async Task LoadSimilarAsync()
    {
        if (Movie is null) return;
        try
        {
            var result = await Http.GetFromJsonAsync<List<MovieDto>>($"api/movies/{MovieId}/similar?limit=12");
            SimilarMovies = result ?? new();
        }
        catch
        {
            SimilarMovies = new();
        }
    }

    private async Task LoadMovieAsync()
    {
        try
        {
            Movie = await Http.GetFromJsonAsync<MovieDto>($"api/movies/{MovieId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MovieDetail] Failed to load movie {MovieId}: {ex.Message}");
            Movie = null;
        }
    }

    private async Task LoadUserEntryAsync()
    {
        try
        {
            var response = await AuthService.SendAuthorizedAsync(
                () => new HttpRequestMessage(HttpMethod.Get, $"api/user/movies/{MovieId}"));

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                HasUserEntry = false;
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[MovieDetail] User entry returned {response.StatusCode}");
                return;
            }

            var entry = await response.Content.ReadFromJsonAsync<UserMovieEntryDto>();
            if (entry is null) return;

            HasUserEntry = true;
            SelectedStatus = entry.Status;
            SelectedRating = entry.UserRating;
            UserNotes = entry.Notes ?? "";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MovieDetail] Failed to load user entry: {ex.Message}");
        }
    }

    protected void SelectStatus(string status)
    {
        SelectedStatus = status;
        IsListMenuOpen = false;
        SaveMessage = null;
    }

    protected void SelectRating(int rating)
    {
        SelectedRating = SelectedRating == rating ? null : rating;
        SaveMessage = null;
    }

    protected void ClearRating()
    {
        SelectedRating = null;
        SaveMessage = null;
    }

    protected async Task SaveUserMovieAsync()
    {
        if (!AuthService.IsLoggedIn) return;

        IsSaving = true;
        SaveMessage = null;

        try
        {
            var notes = string.IsNullOrWhiteSpace(UserNotes) ? null : UserNotes.Trim();
            var response = await AuthService.SendAuthorizedAsync(() =>
            {
                var request = new HttpRequestMessage(HttpMethod.Put, $"api/user/movies/{MovieId}")
                {
                    Content = JsonContent.Create(new
                    {
                        Status = SelectedStatus,
                        UserRating = SelectedRating,
                        Notes = notes,
                        ClearUserRating = SelectedRating is null,
                        ClearNotes = notes is null
                    })
                };
                return request;
            });

            if (response.IsSuccessStatusCode)
            {
                HasUserEntry = true;
                SaveMessage = "Salvato";
            }
            else
            {
                var body = await response.Content.ReadAsStringAsync();
                SaveMessage = "Errore salvataggio";
                Console.WriteLine($"[MovieDetail] Save failed {response.StatusCode}: {body}");
            }
        }
        finally
        {
            IsSaving = false;
        }
    }

    protected async Task RemoveFromLibraryAsync()
    {
        if (!AuthService.IsLoggedIn) return;

        IsSaving = true;
        SaveMessage = null;

        try
        {
            var response = await AuthService.SendAuthorizedAsync(
                () => new HttpRequestMessage(HttpMethod.Delete, $"api/user/movies/{MovieId}"));

            if (response.IsSuccessStatusCode)
            {
                HasUserEntry = false;
                SelectedStatus = "to_watch";
                SelectedRating = null;
                UserNotes = "";
                SaveMessage = "Rimosso";
            }
            else
            {
                SaveMessage = "Errore rimozione";
            }
        }
        finally
        {
            IsSaving = false;
        }
    }

    protected async Task HandleLogout()
    {
        await AuthService.LogoutAsync();
        HasUserEntry = false;
        SelectedStatus = "to_watch";
        SelectedRating = null;
        UserNotes = "";
        StateHasChanged();
    }

    protected void GoBack()
    {
        Navigation.NavigateTo("/");
    }

    protected void ToggleListMenu()
    {
        IsListMenuOpen = !IsListMenuOpen;
    }

    protected void SetTab(string tab) => ActiveTab = tab;

    protected string GetScoreColorVar()
    {
        var pct = GetRatingPercent();
        if (pct >= 75) return "#62c96c";
        if (pct >= 60) return "#c49529";
        return "#d44a4a";
    }

    protected string GetSelectedStatusLabel() =>
        StatusOptions.FirstOrDefault(o => o.Value == SelectedStatus).Label ?? "Scegli lista";

    protected static string GetStatusIcon(string status) => status switch
    {
        "to_watch" => "+",
        "watching" => "▶",
        "watched" => "✓",
        "dropped" => "×",
        _ => "-"
    };

    protected string GetOverview() =>
        string.IsNullOrWhiteSpace(Movie?.Overview)
            ? "No overview available for this title."
            : Movie.Overview!;

    protected IEnumerable<GenreTileModel> GetGenreTiles()
    {
        if (Movie?.Genres is null) yield break;

        foreach (var genre in Movie.Genres.Take(6))
        {
            if (GenreTiles.TryGetValue(genre, out var tile))
            {
                yield return tile;
                continue;
            }

            yield return new GenreTileModel(
                genre,
                "#d44a4a",
                "/images/genres/eye.svg",
                "/images/genres/Horror.png");
        }
    }

    protected string? GetBackdropPath() =>
        string.IsNullOrWhiteSpace(Movie?.BackdropPath) ? null : Movie.BackdropPath;

    protected static string GetTmdbImageUrl(string? path, string size) =>
        string.IsNullOrWhiteSpace(path) ? "" : $"https://image.tmdb.org/t/p/{size}{path}";

    protected string GetTmdbTitleUrl()
    {
        if (Movie?.TmdbId is > 0)
            return $"https://www.themoviedb.org/{(Movie.MediaType == "tv" ? "tv" : "movie")}/{Movie.TmdbId}";

        return Movie is null
            ? "https://www.themoviedb.org"
            : $"https://www.themoviedb.org/search?query={Uri.EscapeDataString(Movie.Title)}";
    }

    protected static string FormatRuntime(short minutes)
    {
        if (minutes < 60) return $"{minutes}min";
        var h = minutes / 60;
        var m = minutes % 60;
        return m == 0 ? $"{h}h" : $"{h}h {m}min";
    }

    protected static string FormatScore(decimal? score) =>
        score.HasValue && score > 0 ? score.Value.ToString("0.0") : "N/A";

    protected static string FormatVoteCount(int? votes) =>
        votes.HasValue ? votes.Value.ToString("N0") : "N/A";

    protected static string FormatPopularity(decimal? popularity) =>
        popularity.HasValue ? popularity.Value.ToString("0") : "N/A";

    protected static string FormatMoney(long? amount) =>
        amount.HasValue && amount.Value > 0 ? $"${amount.Value:N0}" : "N/D";


    protected static string FormatLanguage(string? language) =>
        string.IsNullOrWhiteSpace(language) ? "N/D" : language.ToUpperInvariant();

    protected static string FormatCountry(string? countries)
    {
        var first = SplitCsv(countries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(first) ? "N/D" : first;
    }

    protected string GetHomepageLabel() =>
        string.IsNullOrWhiteSpace(Movie?.Homepage) ? "N/D" : "official site ↗";

    protected decimal GetObscurityValue()
    {
        var votes = Movie?.VoteCount ?? 0;
        if (votes <= 0) return 0;
        return Math.Round((decimal)Math.Log10(votes), 2);
    }

    protected string FormatRuntimePressure() =>
        ((Movie?.RuntimeMinutes ?? 0) / 180.0m).ToString("0.00");

    protected string GetRuntimeLabel()
    {
        var runtime = Movie?.RuntimeMinutes ?? 0;
        if (runtime <= 0) return "Unknown";
        if (runtime <= 95) return "Lean";
        if (runtime <= 125) return "Optimal";
        return "Marathon";
    }

    protected static string GetInitials(string value)
    {
        var initials = SplitCsv(value.Replace(" ", ","))
            .Where(part => part.Length > 0)
            .Take(2)
            .Select(part => char.ToUpperInvariant(part[0]));

        var result = string.Concat(initials);
        return string.IsNullOrWhiteSpace(result) ? "?" : result;
    }

    protected static IReadOnlyList<string> SplitCsvForView(string? value) =>
        SplitCsv(value).ToList();

    protected static string FormatCsvValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "N/D";
        var parts = SplitCsv(value).Take(4).ToList();
        return parts.Count == 0 ? "N/D" : string.Join(", ", parts);
    }

    protected static string FormatProviderType(string type) => type switch
    {
        "stream" => "Streaming",
        "ads" => "Gratis con pubblicità",
        "rent" => "Noleggio",
        "buy" => "Acquisto",
        _ => type
    };

    protected bool IsPositiveSaveMessage() =>
        SaveMessage is "Salvato" or "Rimosso";

    protected IEnumerable<(string Role, string Name)> GetPeopleCards()
    {
        foreach (var name in SplitCsv(Movie?.Director).Take(3))
            yield return (Movie?.MediaType == "tv" ? "Creator" : "Regia", name);

        foreach (var name in SplitCsv(Movie?.CastList).Take(6))
            yield return ("Cast", name);

        foreach (var name in SplitCsv(Movie?.Writers).Take(3))
            yield return ("Sceneggiatura", name);

        foreach (var name in SplitCsv(Movie?.DirectorOfPhotography).Take(2))
            yield return ("Fotografia", name);

        foreach (var name in SplitCsv(Movie?.MusicComposer).Take(2))
            yield return ("Musica", name);
    }

    protected bool HasPeopleCards() => GetPeopleCards().Any();

    private static IEnumerable<string> SplitCsv(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) yield break;

        foreach (var part in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!string.IsNullOrWhiteSpace(part))
                yield return part;
        }
    }

    protected int GetRatingPercent()
    {
        if (Movie?.VoteAverage is null || Movie.VoteAverage <= 0) return 0;
        return Math.Clamp((int)Math.Round(Movie.VoteAverage.Value * 10), 0, 100);
    }

    protected string FormatScorePercent() =>
        GetRatingPercent() > 0 ? GetRatingPercent().ToString() : "--";

    protected int GetConfidencePercent()
    {
        if (Movie?.VoteAverage is null || Movie.VoteAverage <= 0) return 8;
        return Math.Clamp((int)Math.Round(Movie.VoteAverage.Value * 10), 8, 100);
    }

    protected int GetObscurityPercent()
    {
        var votes = Movie?.VoteCount ?? 0;
        if (votes <= 0) return 90;
        var normalized = Math.Log10(votes + 1) / 5.0;
        return Math.Clamp(100 - (int)Math.Round(normalized * 100), 8, 96);
    }

    protected int GetRuntimePercent()
    {
        var runtime = Movie?.RuntimeMinutes ?? 0;
        if (runtime <= 0) return 12;
        return Math.Clamp((int)Math.Round(runtime / 180.0 * 100), 10, 100);
    }
}
