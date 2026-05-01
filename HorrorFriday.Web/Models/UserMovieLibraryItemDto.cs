namespace HorrorFriday.Web.Models;

public class UserMovieLibraryItemDto
{
    public MovieDto Movie { get; set; } = new();
    public string Status { get; set; } = string.Empty;
    public short? UserRating { get; set; }
    public string? Notes { get; set; }
    public DateTime AddedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
