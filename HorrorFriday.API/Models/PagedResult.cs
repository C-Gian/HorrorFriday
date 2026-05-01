namespace HorrorFriday.API.Models;

/// <summary>
/// Wraps a list of results with pagination info.
/// The frontend needs to know not just the movies on this page,
/// but also how many total results exist and how many pages there are.
/// 
/// The "T" makes this generic - we can use it for movies, TV shows,
/// or anything else that needs pagination.
/// </summary>
public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}