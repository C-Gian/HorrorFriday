using System.Security.Claims;
using HorrorFriday.API.Models;
using HorrorFriday.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HorrorFriday.API.Controllers;

[ApiController]
[Route("api/user/stats")]
[Authorize]
public class UserStatsController : ControllerBase
{
    private readonly UserStatsService _statsService;

    public UserStatsController(UserStatsService statsService)
    {
        _statsService = statsService;
    }

    [HttpGet]
    public async Task<ActionResult<UserStatsDto>> GetStats()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(claim, out var userId)) return Unauthorized();

        var stats = await _statsService.GetStatsAsync(userId);
        return Ok(stats);
    }
}
