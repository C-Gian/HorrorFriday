using Google.Apis.Auth;
using HorrorFriday.API.Models;
using HorrorFriday.API.Services;
using Microsoft.AspNetCore.Mvc;

namespace HorrorFriday.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly TokenService _tokenService;
    private readonly IConfiguration _config;

    public AuthController(AuthService authService, TokenService tokenService, IConfiguration config)
    {
        _authService = authService;
        _tokenService = tokenService;
        _config = config;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        try
        {
            var user = await _authService.RegisterAsync(request);
            if (user is null) return BadRequest("Registration failed.");

            var token = _tokenService.GenerateJwtToken(user);
            var refreshToken = _tokenService.GenerateRefreshToken();
            await _tokenService.StoreRefreshTokenAsync(user.Id, refreshToken);

            return Ok(new AuthResponse { Token = token, RefreshToken = refreshToken, User = user });
        }
        catch
        {
            return BadRequest("Username or email already taken.");
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var (user, match) = await _authService.LoginAsync(request);
        if (user is null || !match)
            return Unauthorized("Invalid credentials.");

        var token = _tokenService.GenerateJwtToken(user);
        var refreshToken = _tokenService.GenerateRefreshToken();
        await _tokenService.StoreRefreshTokenAsync(user.Id, refreshToken);

        return Ok(new AuthResponse { Token = token, RefreshToken = refreshToken, User = user });
    }

    [HttpPost("google")]
    public async Task<IActionResult> Google([FromBody] GoogleLoginRequest request)
    {
        GoogleJsonWebSignature.Payload payload;
        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { _config["Google:ClientId"] }
            };
            payload = await GoogleJsonWebSignature.ValidateAsync(request.IdToken, settings);
        }
        catch
        {
            return Unauthorized("Invalid Google token.");
        }

        var user = await _authService.GoogleLoginOrRegisterAsync(payload.Subject, payload.Email, payload.Name ?? "");
        if (user is null) return StatusCode(500, "Failed to create user.");

        var token = _tokenService.GenerateJwtToken(user);
        var refreshToken = _tokenService.GenerateRefreshToken();
        await _tokenService.StoreRefreshTokenAsync(user.Id, refreshToken);

        return Ok(new AuthResponse { Token = token, RefreshToken = refreshToken, User = user });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request)
    {
        var userId = await _tokenService.ValidateRefreshTokenAsync(request.RefreshToken);
        if (userId is null)
            return Unauthorized("Invalid refresh token.");

        var user = await _authService.GetUserByIdAsync(userId.Value);
        if (user is null)
            return Unauthorized("User not found.");

        var token = _tokenService.GenerateJwtToken(user);
        var refreshToken = _tokenService.GenerateRefreshToken();
        await _tokenService.StoreRefreshTokenAsync(user.Id, refreshToken);

        return Ok(new AuthResponse { Token = token, RefreshToken = refreshToken, User = user });
    }
}
