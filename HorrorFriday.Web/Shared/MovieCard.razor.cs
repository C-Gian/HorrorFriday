using HorrorFriday.Web.Models;
using Microsoft.AspNetCore.Components;

namespace HorrorFriday.Web.Shared;

public partial class MovieCard : ComponentBase
{
    [Parameter, EditorRequired]
    public MovieDto Movie { get; set; } = new();

    [Parameter]
    public bool IsLoggedIn { get; set; }

    [Parameter]
    public string? Status { get; set; }

    [Parameter]
    public bool IsStatusPickerOpen { get; set; }

    [Parameter]
    public EventCallback<int> OnNavigate { get; set; }

    [Parameter]
    public EventCallback<int> OnToggleStatusPicker { get; set; }

    [Parameter]
    public EventCallback OnCloseStatusPicker { get; set; }

    [Parameter]
    public EventCallback<MovieStatusChange> OnStatusChanged { get; set; }

    protected bool HasStatus => !string.IsNullOrEmpty(Status);

    protected static readonly List<(string Value, string Label)> StatusOptions = new()
    {
        ("to_watch", "To Watch"),
        ("watching", "Watching"),
        ("watched", "Watched"),
        ("dropped", "Dropped")
    };

    protected Task HandleNavigate() => OnNavigate.InvokeAsync(Movie.Id);

    protected Task HandleToggleStatusPicker() => OnToggleStatusPicker.InvokeAsync(Movie.Id);

    protected Task HandleMouseLeave() =>
        IsStatusPickerOpen ? OnCloseStatusPicker.InvokeAsync() : Task.CompletedTask;

    protected Task HandleStatusChanged(string? status) =>
        OnStatusChanged.InvokeAsync(new MovieStatusChange(Movie.Id, status));

    protected static string GetStatusLabel(string status) => status switch
    {
        "to_watch" => "To Watch",
        "watching" => "Watching",
        "watched" => "Watched",
        "dropped" => "Dropped",
        _ => status
    };

    protected static string TruncateText(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return "No overview available.";
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}

public record MovieStatusChange(int MovieId, string? Status);
