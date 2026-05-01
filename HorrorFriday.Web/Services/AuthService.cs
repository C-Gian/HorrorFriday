using System.Net.Http.Json;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.JSInterop;

namespace HorrorFriday.Web.Services;

public class AuthService
{
    private readonly HttpClient _http;
    private readonly IJSRuntime _js;

    private string? _token;
    private string? _refreshToken;
    private UserInfo? _currentUser;
    private bool _initialized;
    private bool _useLocalStorage = true;

    public AuthService(HttpClient http, IJSRuntime js)
    {
        _http = http;
        _js = js;
    }

    public bool IsLoggedIn => _token is not null;
    public UserInfo? CurrentUser => _currentUser;
    public string? Token => _token;

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        // Check sessionStorage first (not-remembered session), then localStorage
        var token = await _js.InvokeAsync<string?>("sessionStorage.getItem", "hf_token");
        if (token is not null)
        {
            _useLocalStorage = false;
        }
        else
        {
            token = await _js.InvokeAsync<string?>("localStorage.getItem", "hf_token");
            if (token is null) return;
            _useLocalStorage = true;
        }

        var storage = _useLocalStorage ? "localStorage" : "sessionStorage";
        _refreshToken = await _js.InvokeAsync<string?>($"{storage}.getItem", "hf_refresh_token");
        var userId = await _js.InvokeAsync<string?>($"{storage}.getItem", "hf_userid");
        var username = await _js.InvokeAsync<string?>($"{storage}.getItem", "hf_username");
        var email = await _js.InvokeAsync<string?>($"{storage}.getItem", "hf_email");
        var hasPassword = await _js.InvokeAsync<string?>($"{storage}.getItem", "hf_has_password");

        _token = token;
        _currentUser = new UserInfo
        {
            Id = int.TryParse(userId, out var id) ? id : 0,
            Username = username ?? "",
            Email = email ?? "",
            HasPassword = hasPassword == "true"
        };
    }

    public async Task<bool> LoginAsync(string email, string password, bool rememberMe = true)
    {
        var response = await _http.PostAsJsonAsync("api/auth/login", new { Email = email, Password = password });
        if (!response.IsSuccessStatusCode) return false;

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        if (auth is null) return false;

        _useLocalStorage = rememberMe;
        await SaveSessionAsync(auth);
        return true;
    }

    public async Task<(bool success, string? error)> GoogleLoginAsync(string idToken)
    {
        var response = await _http.PostAsJsonAsync("api/auth/google", new { IdToken = idToken });
        if (!response.IsSuccessStatusCode)
        {
            var msg = await response.Content.ReadAsStringAsync();
            return (false, msg);
        }

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        if (auth is null) return (false, "Unknown error.");

        _useLocalStorage = true; // Google login always remembered
        await SaveSessionAsync(auth);
        return (true, null);
    }

    public async Task<(bool success, string? error)> RegisterAsync(string username, string email, string password)
    {
        var response = await _http.PostAsJsonAsync("api/auth/register",
            new { Username = username, Email = email, Password = password });

        if (!response.IsSuccessStatusCode)
        {
            var msg = await response.Content.ReadAsStringAsync();
            return (false, msg);
        }

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        if (auth is null) return (false, "Unknown error.");

        _useLocalStorage = true;
        await SaveSessionAsync(auth);
        return (true, null);
    }

    public async Task<(bool success, string? error)> ForgotPasswordAsync(string email)
    {
        var response = await _http.PostAsJsonAsync("api/account/forgot-password", new { Email = email });
        return response.IsSuccessStatusCode ? (true, null) : (false, await response.Content.ReadAsStringAsync());
    }

    public async Task<(bool success, string? error)> ResetPasswordAsync(string token, string newPassword)
    {
        var response = await _http.PostAsJsonAsync("api/account/reset-password",
            new { Token = token, NewPassword = newPassword });
        return response.IsSuccessStatusCode ? (true, null) : (false, await response.Content.ReadAsStringAsync());
    }

    public async Task<(bool success, string? error)> ChangePasswordAsync(string currentPassword, string newPassword)
    {
        var response = await SendAuthorizedAsync(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Put, "api/account/change-password");
            req.Content = JsonContent.Create(new { CurrentPassword = currentPassword, NewPassword = newPassword });
            return req;
        });
        return response.IsSuccessStatusCode ? (true, null) : (false, await response.Content.ReadAsStringAsync());
    }

    public async Task<(bool success, string? error)> ChangeEmailAsync(string newEmail, string password)
    {
        var response = await SendAuthorizedAsync(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Put, "api/account/change-email");
            req.Content = JsonContent.Create(new { NewEmail = newEmail, Password = password });
            return req;
        });
        if (!response.IsSuccessStatusCode)
            return (false, await response.Content.ReadAsStringAsync());

        var user = await response.Content.ReadFromJsonAsync<UserDto>();
        if (user is not null && _currentUser is not null)
        {
            _currentUser = _currentUser with { Email = user.Email };
            await SetStorageAsync("hf_email", user.Email);
        }
        return (true, null);
    }

    public async Task<(bool success, string? error)> ChangeUsernameAsync(string newUsername)
    {
        var response = await SendAuthorizedAsync(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Put, "api/account/change-username");
            req.Content = JsonContent.Create(new { NewUsername = newUsername });
            return req;
        });
        if (!response.IsSuccessStatusCode)
            return (false, await response.Content.ReadAsStringAsync());

        var user = await response.Content.ReadFromJsonAsync<UserDto>();
        if (user is not null && _currentUser is not null)
        {
            _currentUser = _currentUser with { Username = user.Username };
            await SetStorageAsync("hf_username", user.Username);
        }
        return (true, null);
    }

    public async Task LogoutAsync()
    {
        _token = null;
        _refreshToken = null;
        _currentUser = null;
        foreach (var key in new[] { "hf_token", "hf_refresh_token", "hf_userid", "hf_username", "hf_email", "hf_has_password" })
        {
            await _js.InvokeVoidAsync("localStorage.removeItem", key);
            await _js.InvokeVoidAsync("sessionStorage.removeItem", key);
        }
    }

    public async Task<HttpResponseMessage> SendAuthorizedAsync(Func<HttpRequestMessage> requestFactory)
    {
        var response = await SendWithTokenAsync(requestFactory());
        if (response.StatusCode != HttpStatusCode.Unauthorized || !await RefreshAsync())
            return response;

        response.Dispose();
        return await SendWithTokenAsync(requestFactory());
    }

    private async Task<HttpResponseMessage> SendWithTokenAsync(HttpRequestMessage request)
    {
        if (_token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

        return await _http.SendAsync(request);
    }

    private async Task<bool> RefreshAsync()
    {
        if (string.IsNullOrWhiteSpace(_refreshToken))
        {
            await LogoutAsync();
            return false;
        }

        var response = await _http.PostAsJsonAsync("api/auth/refresh", new { RefreshToken = _refreshToken });
        if (!response.IsSuccessStatusCode)
        {
            await LogoutAsync();
            return false;
        }

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        if (auth is null)
        {
            await LogoutAsync();
            return false;
        }

        await SaveSessionAsync(auth);
        return true;
    }

    private async Task SaveSessionAsync(AuthResponse auth)
    {
        _token = auth.Token;
        _refreshToken = auth.RefreshToken;
        _currentUser = new UserInfo
        {
            Id = auth.User.Id,
            Username = auth.User.Username,
            Email = auth.User.Email,
            HasPassword = auth.User.HasPassword
        };

        await SetStorageAsync("hf_token", auth.Token);
        await SetStorageAsync("hf_refresh_token", auth.RefreshToken);
        await SetStorageAsync("hf_userid", auth.User.Id.ToString());
        await SetStorageAsync("hf_username", auth.User.Username);
        await SetStorageAsync("hf_email", auth.User.Email);
        await SetStorageAsync("hf_has_password", auth.User.HasPassword ? "true" : "false");
    }

    private async Task SetStorageAsync(string key, string value)
    {
        var storage = _useLocalStorage ? "localStorage" : "sessionStorage";
        await _js.InvokeVoidAsync($"{storage}.setItem", key, value);
    }

    public record UserInfo
    {
        public int Id { get; init; }
        public string Username { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public bool HasPassword { get; init; }
    }

    private class AuthResponse
    {
        public string Token { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public UserDto User { get; set; } = new();
    }

    private class UserDto
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool HasPassword { get; set; }
    }
}
