namespace HorrorFriday.Web.Models;

public class UserMovieEntryDto
{
    public int MovieId { get; set; }
    public string Status { get; set; } = string.Empty;
    public short? UserRating { get; set; }
    public string? Notes { get; set; }
    public DateTime AddedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
