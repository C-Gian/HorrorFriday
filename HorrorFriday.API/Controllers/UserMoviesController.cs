using System.Security.Claims;
using HorrorFriday.API.Models;
using HorrorFriday.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HorrorFriday.API.Controllers;

[ApiController]
[Route("api/user/movies")]
[Authorize]
public class UserMoviesController : ControllerBase
{
    private static readonly HashSet<string> ValidStatuses = ["to_watch", "watched", "watching", "dropped"];

    private readonly UserMovieService _userMovieService;

    public UserMoviesController(UserMovieService userMovieService)
    {
        _userMovieService = userMovieService;
    }

    private int GetUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> GetLibrary([FromQuery] string? status = null)
    {
        if (!string.IsNullOrWhiteSpace(status) && !ValidStatuses.Contains(status))
            return BadRequest("Invalid status.");

        var library = await _userMovieService.GetLibraryAsync(GetUserId(), status);
        return Ok(library);
    }

    [HttpPost("statuses")]
    public async Task<IActionResult> GetStatuses([FromBody] UserMovieStatusesRequest request)
    {
        var statuses = await _userMovieService.GetStatusesAsync(GetUserId(), request.MovieIds);
        return Ok(statuses);
    }

    [HttpGet("{movieId:int}")]
    public async Task<IActionResult> GetEntry(int movieId)
    {
        var entry = await _userMovieService.GetEntryAsync(GetUserId(), movieId);
        return entry is null ? NotFound() : Ok(entry);
    }

    [HttpPut("{movieId:int}")]
    public async Task<IActionResult> UpsertStatus(int movieId, [FromBody] UpdateUserMovieRequest request)
    {
        if (!ValidStatuses.Contains(request.Status))
            return BadRequest("Invalid status.");

        try
        {
            await _userMovieService.UpsertEntryAsync(GetUserId(), movieId, request);
            return Ok();
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"DB error: {ex.Message}");
        }
    }

    [HttpDelete("{movieId:int}")]
    public async Task<IActionResult> Remove(int movieId)
    {
        await _userMovieService.RemoveAsync(GetUserId(), movieId);
        return Ok();
    }
}
