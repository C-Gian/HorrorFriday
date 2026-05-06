using HorrorFriday.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace HorrorFriday.Web.Shared;

public partial class AppNavbar : ComponentBase, IAsyncDisposable
{
    [Inject] protected AuthService AuthService { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    [Parameter]
    public EventCallback OnLoggedOut { get; set; }

    protected bool IsMenuOpen { get; set; }

    private DotNetObjectReference<AppNavbar>? _dotNetRef;

    protected override async Task OnInitializedAsync()
    {
        AuthService.OnAuthStateChanged += StateHasChanged;
        await AuthService.InitializeAsync();
    }

    protected async Task ToggleMenu()
    {
        IsMenuOpen = !IsMenuOpen;
        if (IsMenuOpen)
        {
            _dotNetRef ??= DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("userMenu.open", _dotNetRef);
        }
        else
        {
            await JS.InvokeVoidAsync("userMenu.close");
        }
    }

    [JSInvokable]
    public void CloseMenu()
    {
        IsMenuOpen = false;
        StateHasChanged();
    }

    protected async Task LogoutAsync()
    {
        await JS.InvokeVoidAsync("userMenu.close");
        IsMenuOpen = false;
        await AuthService.LogoutAsync();
        await OnLoggedOut.InvokeAsync();
        Navigation.NavigateTo("/");
    }

    protected static string GetInitials(AuthService.UserInfo user)
    {
        if (string.IsNullOrWhiteSpace(user.Username)) return "HF";
        var parts = user.Username.Split([' ', '.', '_', '-'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
            return $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant();
        return user.Username[..Math.Min(2, user.Username.Length)].ToUpperInvariant();
    }

    public async ValueTask DisposeAsync()
    {
        AuthService.OnAuthStateChanged -= StateHasChanged;
        await JS.InvokeVoidAsync("userMenu.close");
        _dotNetRef?.Dispose();
    }
}
