namespace HorrorFriday.API.Models;

public class UpdateUserMovieRequest
{
    public string Status { get; set; } = string.Empty;
    public short? UserRating { get; set; }
    public string? Notes { get; set; }
    public bool ClearUserRating { get; set; }
    public bool ClearNotes { get; set; }
}

public class UserMovieStatusesRequest
{
    public List<int> MovieIds { get; set; } = new();
}

public class UserMovieEntryDto
{
    public int MovieId { get; set; }
    public string Status { get; set; } = string.Empty;
    public short? UserRating { get; set; }
    public string? Notes { get; set; }
    public DateTime AddedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class UserMovieLibraryItemDto
{
    public MovieDto Movie { get; set; } = new();
    public string Status { get; set; } = string.Empty;
    public short? UserRating { get; set; }
    public string? Notes { get; set; }
    public DateTime AddedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
