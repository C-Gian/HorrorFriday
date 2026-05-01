using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using HorrorFriday.Web.Services;

namespace HorrorFriday.Web.Pages;

public partial class Login : ComponentBase, IDisposable
{
    [Inject] private AuthService AuthService { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private DotNetObjectReference<Login>? _dotNetRef;

    protected bool IsRegister { get; set; } = false;
    protected string Username { get; set; } = string.Empty;
    protected string Email { get; set; } = string.Empty;
    protected string Password { get; set; } = string.Empty;
    protected string? ErrorMessage { get; set; }
    protected bool IsLoading { get; set; }
    protected bool ShowPassword { get; set; }
    protected bool RememberMe { get; set; } = true;

    protected override async Task OnInitializedAsync()
    {
        await AuthService.InitializeAsync();
        if (AuthService.IsLoggedIn)
            Navigation.NavigateTo("/");
    }

    protected async Task Submit()
    {
        ErrorMessage = null;
        IsLoading = true;
        StateHasChanged();

        try
        {
            if (IsRegister)
            {
                if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
                {
                    ErrorMessage = "All fields are required.";
                    return;
                }

                var (success, error) = await AuthService.RegisterAsync(Username.Trim(), Email.Trim(), Password);
                if (!success)
                {
                    ErrorMessage = error ?? "Registration failed.";
                    return;
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
                {
                    ErrorMessage = "Email and password are required.";
                    return;
                }

                var ok = await AuthService.LoginAsync(Email.Trim(), Password, RememberMe);
                if (!ok)
                {
                    ErrorMessage = "Invalid email or password.";
                    return;
                }
            }

            Navigation.NavigateTo("/");
        }
        finally
        {
            IsLoading = false;
            StateHasChanged();
        }
    }

    protected async Task HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
            await Submit();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        _dotNetRef = DotNetObjectReference.Create(this);
        await JS.InvokeVoidAsync("googleAuth.initAndRender", "google-btn-container", _dotNetRef);
    }

    [JSInvokable]
    public async Task OnGoogleCredential(string idToken)
    {
        ErrorMessage = null;
        IsLoading = true;
        StateHasChanged();
        try
        {
            var (success, error) = await AuthService.GoogleLoginAsync(idToken);
            if (success)
                Navigation.NavigateTo("/");
            else
                ErrorMessage = error ?? "Google login failed.";
        }
        finally
        {
            IsLoading = false;
            StateHasChanged();
        }
    }

    public void Dispose()
    {
        _dotNetRef?.Dispose();
    }
}
