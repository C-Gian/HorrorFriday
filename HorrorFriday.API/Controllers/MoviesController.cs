using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using HorrorFriday.API.Models;
using HorrorFriday.API.Services;

namespace HorrorFriday.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MoviesController : ControllerBase
{
    private readonly MovieService _movieService;

    public MoviesController(MovieService movieService)
    {
        _movieService = movieService;
    }

    [HttpPost("search")]
    public async Task<ActionResult<PagedResult<MovieDto>>> Search([FromBody] SearchRequest request)
    {
        // Read userId from JWT if the caller sent an Authorization header.
        // The endpoint stays anonymous — userId is just null for unauthenticated requests.
        int? userId = null;
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(claim, out var id)) userId = id;

        var result = await _movieService.SearchAsync(request, userId);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<MovieDto>> GetById(int id)
    {
        var movie = await _movieService.GetByIdAsync(id);
        if (movie == null) return NotFound();
        return Ok(movie);
    }

    [HttpGet("genres")]
    public async Task<ActionResult<List<string>>> GetGenres()
    {
        var genres = await _movieService.GetAllGenresAsync();
        return Ok(genres);
    }
}
