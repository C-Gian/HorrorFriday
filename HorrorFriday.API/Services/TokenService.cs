using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HorrorFriday.API.Models;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace HorrorFriday.API.Services;

public class TokenService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly string _secret;
    private readonly string _issuer;

    public TokenService(NpgsqlDataSource dataSource, IConfiguration config)
    {
        _dataSource = dataSource;
        _secret = config["Jwt:Secret"] ?? throw new InvalidOperationException("Jwt:Secret not configured");
        _issuer = config["Jwt:Issuer"] ?? "HorrorFriday";
    }

    public string GenerateJwtToken(UserDto user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.Username)
        };

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _issuer,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    public async Task StoreRefreshTokenAsync(int userId, string refreshToken)
    {
        const string sql = @"
            INSERT INTO refresh_tokens (user_id, token, expires_at, created_at)
            VALUES ($1, $2, NOW() + INTERVAL '7 days', NOW())";

        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(userId);
        cmd.Parameters.AddWithValue(refreshToken);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int?> ValidateRefreshTokenAsync(string refreshToken)
    {
        const string sql = @"
            SELECT user_id FROM refresh_tokens
            WHERE token = $1 AND expires_at > NOW()";

        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(refreshToken);

        var result = await cmd.ExecuteScalarAsync();
        return result is int id ? id : result is long l ? (int)l : null;
    }
}
