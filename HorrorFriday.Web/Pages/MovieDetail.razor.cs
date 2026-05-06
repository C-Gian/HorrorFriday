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
    protected bool IsLoading { get; set; } = true;
    protected bool IsSaving { get; set; }
    protected bool HasUserEntry { get; set; }
    protected string SelectedStatus { get; set; } = "to_watch";
    protected int? SelectedRating { get; set; }
    protected string UserNotes { get; set; } = "";
    protected string? SaveMessage { get; set; }

    protected static readonly List<(string Value, string Label)> StatusOptions = new()
    {
        ("to_watch", "To Watch"),
        ("watching", "Watching"),
        ("watched", "Watched"),
        ("dropped", "Dropped")
    };

    protected override async Task OnParametersSetAsync()
    {
        IsLoading = true;
        SaveMessage = null;

        await AuthService.InitializeAsync();
        await LoadMovieAsync();

        if (AuthService.IsLoggedIn)
            await LoadUserEntryAsync();

        IsLoading = false;
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
                SaveMessage = "Saved";
            }
            else
            {
                var body = await response.Content.ReadAsStringAsync();
                SaveMessage = "Save failed";
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
                SaveMessage = "Removed";
            }
            else
            {
                SaveMessage = "Remove failed";
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

    protected string GetOverview() =>
        string.IsNullOrWhiteSpace(Movie?.Overview)
            ? "No overview available for this title."
            : Movie.Overview!;

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
