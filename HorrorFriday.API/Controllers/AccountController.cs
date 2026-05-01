using System.Security.Claims;
using HorrorFriday.API.Helpers;
using HorrorFriday.API.Models;
using HorrorFriday.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HorrorFriday.API.Controllers;

[ApiController]
[Route("api/account")]
public class AccountController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly PasswordResetService _passwordResetService;

    public AccountController(AuthService authService, PasswordResetService passwordResetService)
    {
        _authService = authService;
        _passwordResetService = passwordResetService;
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest("L'email è obbligatoria.");
        await _passwordResetService.RequestResetAsync(request.Email.Trim());
        // Always return OK to avoid revealing whether the email exists
        return Ok();
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.NewPassword))
            return BadRequest("Token e nuova password sono obbligatori.");

        var (valid, policyError) = PasswordPolicy.Validate(request.NewPassword);
        if (!valid) return BadRequest(policyError);

        var (success, error) = await _passwordResetService.ResetPasswordAsync(request.Token, request.NewPassword);
        if (!success) return BadRequest(error);
        return Ok();
    }

    [Authorize]
    [HttpPut("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var (valid, policyError) = PasswordPolicy.Validate(request.NewPassword);
        if (!valid) return BadRequest(policyError);

        var (success, error) = await _authService.ChangePasswordAsync(userId, request.CurrentPassword, request.NewPassword);
        if (!success) return BadRequest(error);
        return Ok();
    }

    [Authorize]
    [HttpPut("change-email")]
    public async Task<IActionResult> ChangeEmail([FromBody] ChangeEmailRequest request)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        if (string.IsNullOrWhiteSpace(request.NewEmail))
            return BadRequest("La nuova email è obbligatoria.");

        var (success, error) = await _authService.ChangeEmailAsync(userId, request.NewEmail.Trim(), request.Password);
        if (!success) return BadRequest(error);

        var user = await _authService.GetUserByIdAsync(userId);
        return Ok(user);
    }

    [Authorize]
    [HttpPut("change-username")]
    public async Task<IActionResult> ChangeUsername([FromBody] ChangeUsernameRequest request)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        if (string.IsNullOrWhiteSpace(request.NewUsername) || request.NewUsername.Trim().Length < 3)
            return BadRequest("Lo username deve contenere almeno 3 caratteri.");
        if (request.NewUsername.Trim().Length > 32)
            return BadRequest("Lo username non può superare 32 caratteri.");

        var (success, error) = await _authService.ChangeUsernameAsync(userId, request.NewUsername.Trim());
        if (!success) return BadRequest(error);

        var user = await _authService.GetUserByIdAsync(userId);
        return Ok(user);
    }
}
