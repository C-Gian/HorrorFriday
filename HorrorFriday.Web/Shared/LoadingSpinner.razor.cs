using Microsoft.AspNetCore.Components;

namespace HorrorFriday.Web.Shared;

public partial class LoadingSpinner : ComponentBase
{
    /// <summary>
    /// Size of the spinner: "small" (16px, for inline/buttons), 
    /// "medium" (32px, default), or "large" (48px, for full-page loading).
    /// </summary>
    [Parameter]
    public string Size { get; set; } = "medium";

    /// <summary>
    /// Optional message shown below the spinner.
    /// Leave null or empty to show spinner only.
    /// </summary>
    [Parameter]
    public string? Message { get; set; }

    protected string SizeClass => Size.ToLowerInvariant() switch
    {
        "small" => "spinner-sm",
        "large" => "spinner-lg",
        _ => "spinner-md"
    };
}