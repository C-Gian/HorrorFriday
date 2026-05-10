using Microsoft.AspNetCore.Components;

namespace HorrorFriday.Web.Shared;

public partial class GenreTile : ComponentBase
{
    [Parameter, EditorRequired]
    public string Name { get; set; } = string.Empty;

    [Parameter, EditorRequired]
    public string Color { get; set; } = "#d44a4a";

    [Parameter, EditorRequired]
    public string IconUrl { get; set; } = "/images/genres/eye.svg";

    [Parameter, EditorRequired]
    public string BackgroundImageUrl { get; set; } = "/images/genres/Horror.png";

    protected string Style =>
        $"--genre-color:{Color}; --genre-icon:url('{IconUrl}'); --genre-bg:url('{BackgroundImageUrl}');";
}
