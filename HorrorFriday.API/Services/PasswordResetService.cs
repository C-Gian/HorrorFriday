using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace HorrorFriday.API.Services;

public class PasswordResetService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _config;

    public PasswordResetService(NpgsqlDataSource dataSource, IEmailService emailService, IConfiguration config)
    {
        _dataSource = dataSource;
        _emailService = emailService;
        _config = config;
    }

    public async Task RequestResetAsync(string email)
    {
        // Only handle accounts with a password (not Google-only)
        const string findUser = "SELECT id FROM users WHERE email = $1 AND password_hash IS NOT NULL";
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(findUser, conn);
        cmd.Parameters.AddWithValue(email.ToLowerInvariant());
        var result = await cmd.ExecuteScalarAsync();
        // Silently skip — never reveal whether the email exists or is Google-only
        if (result is not int userId) return;

        // Invalidate any existing tokens for this user
        await using var conn2 = await _dataSource.OpenConnectionAsync();
        await using var invalidate = new NpgsqlCommand(
            "UPDATE password_reset_tokens SET used_at = NOW() WHERE user_id = $1 AND used_at IS NULL", conn2);
        invalidate.Parameters.AddWithValue(userId);
        await invalidate.ExecuteNonQueryAsync();

        // Generate a URL-safe random token
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

        const string insert = @"
            INSERT INTO password_reset_tokens (user_id, token_hash, expires_at)
            VALUES ($1, $2, NOW() + INTERVAL '1 hour')";
        await using var conn3 = await _dataSource.OpenConnectionAsync();
        await using var insertCmd = new NpgsqlCommand(insert, conn3);
        insertCmd.Parameters.AddWithValue(userId);
        insertCmd.Parameters.AddWithValue(tokenHash);
        await insertCmd.ExecuteNonQueryAsync();

        var baseUrl = (_config["App:BaseUrl"] ?? "http://localhost:5028").TrimEnd('/');
        var resetLink = $"{baseUrl}/reset-password?token={rawToken}";
        await _emailService.SendPasswordResetAsync(email, resetLink);
    }

    public async Task<(bool success, string? error)> ResetPasswordAsync(string rawToken, string newPassword)
    {
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

        const string findToken = @"
            SELECT id, user_id FROM password_reset_tokens
            WHERE token_hash = $1 AND used_at IS NULL AND expires_at > NOW()";
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(findToken, conn);
        cmd.Parameters.AddWithValue(tokenHash);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return (false, "Il link non è valido o è scaduto.");
        var tokenId = reader.GetInt32(0);
        var userId = reader.GetInt32(1);
        await reader.CloseAsync();

        var newHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await using var conn2 = await _dataSource.OpenConnectionAsync();
        await using var updatePwd = new NpgsqlCommand("UPDATE users SET password_hash = $1 WHERE id = $2", conn2);
        updatePwd.Parameters.AddWithValue(newHash);
        updatePwd.Parameters.AddWithValue(userId);
        await updatePwd.ExecuteNonQueryAsync();

        await using var conn3 = await _dataSource.OpenConnectionAsync();
        await using var markUsed = new NpgsqlCommand(
            "UPDATE password_reset_tokens SET used_at = NOW() WHERE id = $1", conn3);
        markUsed.Parameters.AddWithValue(tokenId);
        await markUsed.ExecuteNonQueryAsync();

        return (true, null);
    }
}
