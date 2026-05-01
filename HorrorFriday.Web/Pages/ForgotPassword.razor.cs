using HorrorFriday.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace HorrorFriday.Web.Pages;

public partial class ForgotPassword : ComponentBase
{
    [Inject] private AuthService AuthService { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    protected string Email { get; set; } = string.Empty;
    protected string? ErrorMessage { get; set; }
    protected bool IsLoading { get; set; }
    protected bool Sent { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await AuthService.InitializeAsync();
        if (AuthService.IsLoggedIn)
            Navigation.NavigateTo("/");
    }

    protected async Task Submit()
    {
        ErrorMessage = null;
        if (string.IsNullOrWhiteSpace(Email))
        {
            ErrorMessage = "Inserisci la tua email.";
            return;
        }

        IsLoading = true;
        StateHasChanged();
        try
        {
            await AuthService.ForgotPasswordAsync(Email.Trim());
            Sent = true;
        }
        finally
        {
            IsLoading = false;
            StateHasChanged();
        }
    }

    protected async Task HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter") await Submit();
    }
}
