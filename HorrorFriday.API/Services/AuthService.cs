using Npgsql;
using HorrorFriday.API.Models;

namespace HorrorFriday.API.Services;

public class AuthService
{
    private readonly NpgsqlDataSource _dataSource;

    public AuthService(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<UserDto?> RegisterAsync(RegisterRequest request)
    {
        var hash = BCrypt.Net.BCrypt.HashPassword(request.Password);

        const string sql = @"
            INSERT INTO users (username, email, password_hash, created_at)
            VALUES ($1, $2, $3, NOW())
            RETURNING id, username, email, display_name";

        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(request.Username);
        cmd.Parameters.AddWithValue(request.Email);
        cmd.Parameters.AddWithValue(hash);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new UserDto
        {
            Id = reader.GetInt32(0),
            Username = reader.GetString(1),
            Email = reader.GetString(2),
            DisplayName = reader.IsDBNull(3) ? null : reader.GetString(3),
            HasPassword = true
        };
    }

    public async Task<(UserDto? user, bool passwordMatch)> LoginAsync(LoginRequest request)
    {
        const string sql = "SELECT id, username, email, display_name, password_hash FROM users WHERE email = $1";

        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(request.Email);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return (null, false);

        var storedHash = reader.IsDBNull(4) ? null : reader.GetString(4);
        if (storedHash is null || !BCrypt.Net.BCrypt.Verify(request.Password, storedHash))
            return (null, false);

        return (new UserDto
        {
            Id = reader.GetInt32(0),
            Username = reader.GetString(1),
            Email = reader.GetString(2),
            DisplayName = reader.IsDBNull(3) ? null : reader.GetString(3),
            HasPassword = true
        }, true);
    }

    public async Task<UserDto?> GoogleLoginOrRegisterAsync(string googleId, string email, string name)
    {
        // Find by google_id
        const string findByGoogle = "SELECT id, username, email, display_name, password_hash IS NOT NULL FROM users WHERE google_id = $1";
        await using (var conn = await _dataSource.OpenConnectionAsync())
        await using (var cmd = new NpgsqlCommand(findByGoogle, conn))
        {
            cmd.Parameters.AddWithValue(googleId);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync()) return MapUser(reader);
        }

        // Find by email and link google_id to existing account
        const string findByEmail = "SELECT id, username, email, display_name, password_hash IS NOT NULL FROM users WHERE email = $1";
        await using (var conn = await _dataSource.OpenConnectionAsync())
        await using (var cmd = new NpgsqlCommand(findByEmail, conn))
        {
            cmd.Parameters.AddWithValue(email);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var existing = MapUser(reader);
                await using var conn2 = await _dataSource.OpenConnectionAsync();
                await using var link = new NpgsqlCommand("UPDATE users SET google_id = $1 WHERE id = $2", conn2);
                link.Parameters.AddWithValue(googleId);
                link.Parameters.AddWithValue(existing.Id);
                await link.ExecuteNonQueryAsync();
                return existing;
            }
        }

        // Create new user
        var suffix = googleId.Length >= 6 ? googleId[^6..] : googleId;
        var username = email.Split('@')[0] + "_" + suffix;
        const string insert = @"
            INSERT INTO users (username, email, password_hash, display_name, google_id, created_at)
            VALUES ($1, $2, NULL, $3, $4, NOW())
            RETURNING id, username, email, display_name, password_hash IS NOT NULL";
        await using (var conn = await _dataSource.OpenConnectionAsync())
        await using (var cmd = new NpgsqlCommand(insert, conn))
        {
            cmd.Parameters.AddWithValue(username);
            cmd.Parameters.AddWithValue(email);
            cmd.Parameters.AddWithValue(string.IsNullOrEmpty(name) ? DBNull.Value : (object)name);
            cmd.Parameters.AddWithValue(googleId);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;
            return MapUser(reader);
        }
    }

    public async Task<(bool success, string? error)> ChangePasswordAsync(int userId, string currentPassword, string newPassword)
    {
        const string getHash = "SELECT password_hash FROM users WHERE id = $1";
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(getHash, conn);
        cmd.Parameters.AddWithValue(userId);
        var hash = await cmd.ExecuteScalarAsync() as string;
        if (hash is null || !BCrypt.Net.BCrypt.Verify(currentPassword, hash))
            return (false, "La password attuale non è corretta.");

        var newHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await using var conn2 = await _dataSource.OpenConnectionAsync();
        await using var update = new NpgsqlCommand("UPDATE users SET password_hash = $1 WHERE id = $2", conn2);
        update.Parameters.AddWithValue(newHash);
        update.Parameters.AddWithValue(userId);
        await update.ExecuteNonQueryAsync();
        return (true, null);
    }

    public async Task<(bool success, string? error)> ChangeEmailAsync(int userId, string newEmail, string password)
    {
        const string getHash = "SELECT password_hash FROM users WHERE id = $1";
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(getHash, conn);
        cmd.Parameters.AddWithValue(userId);
        var hash = await cmd.ExecuteScalarAsync() as string;
        if (hash is null || !BCrypt.Net.BCrypt.Verify(password, hash))
            return (false, "La password non è corretta.");

        const string checkEmail = "SELECT COUNT(1) FROM users WHERE email = $1 AND id != $2";
        await using var conn2 = await _dataSource.OpenConnectionAsync();
        await using var checkCmd = new NpgsqlCommand(checkEmail, conn2);
        checkCmd.Parameters.AddWithValue(newEmail.ToLowerInvariant());
        checkCmd.Parameters.AddWithValue(userId);
        var count = (long)(await checkCmd.ExecuteScalarAsync())!;
        if (count > 0) return (false, "Questa email è già in uso.");

        await using var conn3 = await _dataSource.OpenConnectionAsync();
        await using var update = new NpgsqlCommand("UPDATE users SET email = $1 WHERE id = $2", conn3);
        update.Parameters.AddWithValue(newEmail.ToLowerInvariant());
        update.Parameters.AddWithValue(userId);
        await update.ExecuteNonQueryAsync();
        return (true, null);
    }

    public async Task<(bool success, string? error)> ChangeUsernameAsync(int userId, string newUsername)
    {
        const string check = "SELECT COUNT(1) FROM users WHERE username = $1 AND id != $2";
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(check, conn);
        cmd.Parameters.AddWithValue(newUsername);
        cmd.Parameters.AddWithValue(userId);
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        if (count > 0) return (false, "Questo username è già in uso.");

        await using var conn2 = await _dataSource.OpenConnectionAsync();
        await using var update = new NpgsqlCommand("UPDATE users SET username = $1 WHERE id = $2", conn2);
        update.Parameters.AddWithValue(newUsername);
        update.Parameters.AddWithValue(userId);
        await update.ExecuteNonQueryAsync();
        return (true, null);
    }

    private static UserDto MapUser(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        Username = reader.GetString(1),
        Email = reader.GetString(2),
        DisplayName = reader.IsDBNull(3) ? null : reader.GetString(3),
        HasPassword = reader.GetBoolean(4)
    };

    public async Task<UserDto?> GetUserByIdAsync(int userId)
    {
        const string sql = "SELECT id, username, email, display_name, password_hash IS NOT NULL FROM users WHERE id = $1";

        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(userId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return MapUser(reader);
    }
}
