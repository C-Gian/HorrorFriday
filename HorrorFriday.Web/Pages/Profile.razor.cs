using System.Net;
using System.Net.Http.Json;
using HorrorFriday.Web.Models;
using HorrorFriday.Web.Services;
using Microsoft.AspNetCore.Components;

namespace HorrorFriday.Web.Pages;

public partial class Profile : ComponentBase
{
    [Inject] private AuthService AuthService { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    protected bool IsLoading { get; set; } = true;
    protected List<UserMovieLibraryItemDto> Library { get; set; } = new();
    protected string ActiveFilter { get; set; } = "all";

    protected string Username => AuthService.CurrentUser?.Username ?? "User";
    protected string UserInitials => GetInitials(Username);
    protected int RatedCount => Library.Count(x => x.UserRating.HasValue);
    protected string AverageRatingText =>
        RatedCount == 0 ? "N/A" : Library.Where(x => x.UserRating.HasValue).Average(x => x.UserRating!.Value).ToString("0.0");

    protected List<UserMovieLibraryItemDto> FilteredLibrary =>
        ActiveFilter == "all" ? Library : Library.Where(x => x.Status == ActiveFilter).ToList();

    protected static readonly List<(string Value, string Label)> FilterOptions = new()
    {
        ("all", "All"),
        ("to_watch", "To Watch"),
        ("watching", "Watching"),
        ("watched", "Watched"),
        ("dropped", "Dropped")
    };

    protected override async Task OnInitializedAsync()
    {
        await AuthService.InitializeAsync();
        if (!AuthService.IsLoggedIn)
        {
            Navigation.NavigateTo("/login");
            return;
        }

        await LoadLibraryAsync();
        IsLoading = false;
    }

    private async Task LoadLibraryAsync()
    {
        try
        {
            var response = await AuthService.SendAuthorizedAsync(
                () => new HttpRequestMessage(HttpMethod.Get, "api/user/movies"));

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                Navigation.NavigateTo("/login");
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[Profile] Library returned {response.StatusCode}");
                return;
            }

            Library = await response.Content.ReadFromJsonAsync<List<UserMovieLibraryItemDto>>() ?? new();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Profile] Failed to load library: {ex.Message}");
        }
    }

    protected void SetFilter(string filter)
    {
        ActiveFilter = filter;
    }

    protected int CountByStatus(string status) => Library.Count(x => x.Status == status);

    protected int CountForFilter(string filter) =>
        filter == "all" ? Library.Count : CountByStatus(filter);

    protected void OpenMovie(int movieId)
    {
        Navigation.NavigateTo($"/movie/{movieId}");
    }

    protected static string GetStatusLabel(string status) => status switch
    {
        "to_watch" => "To Watch",
        "watching" => "Watching",
        "watched" => "Watched",
        "dropped" => "Dropped",
        _ => status
    };

    protected static string FormatMovieMeta(UserMovieLibraryItemDto item)
    {
        var parts = new List<string>();
        if (item.Movie.ReleaseYear.HasValue) parts.Add(item.Movie.ReleaseYear.Value.ToString());
        if (item.Movie.RuntimeMinutes.HasValue) parts.Add($"{item.Movie.RuntimeMinutes.Value} min");
        return parts.Count == 0 ? "No metadata" : string.Join(" · ", parts);
    }

    private static string GetInitials(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return "HF";
        return username[..Math.Min(2, username.Length)].ToUpperInvariant();
    }
}
