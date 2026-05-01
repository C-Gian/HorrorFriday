using HorrorFriday.Web.Helpers;
using HorrorFriday.Web.Services;
using Microsoft.AspNetCore.Components;

namespace HorrorFriday.Web.Pages;

public partial class Settings : ComponentBase
{
    [Inject] protected AuthService AuthService { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    protected string UserInitials => string.IsNullOrWhiteSpace(AuthService.CurrentUser?.Username)
        ? "HF"
        : AuthService.CurrentUser.Username[..Math.Min(2, AuthService.CurrentUser.Username.Length)].ToUpperInvariant();

    // --- Username ---
    protected bool ShowUsername { get; set; }
    protected string NewUsername { get; set; } = string.Empty;
    protected string? UsernameError { get; set; }
    protected string? UsernameSuccess { get; set; }
    protected bool UsernameLoading { get; set; }

    // --- Email ---
    protected bool ShowEmail { get; set; }
    protected string NewEmail { get; set; } = string.Empty;
    protected string EmailPassword { get; set; } = string.Empty;
    protected string? EmailError { get; set; }
    protected string? EmailSuccess { get; set; }
    protected bool EmailLoading { get; set; }
    protected bool ShowEmailPassword { get; set; }

    // --- Password ---
    protected bool ShowPassword { get; set; }
    protected string CurrentPassword { get; set; } = string.Empty;
    protected string NewPassword { get; set; } = string.Empty;
    protected string NewPasswordConfirm { get; set; } = string.Empty;
    protected string? PasswordError { get; set; }
    protected string? PasswordSuccess { get; set; }
    protected bool PasswordLoading { get; set; }
    protected bool ShowCurrentPwd { get; set; }
    protected bool ShowNewPwd { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await AuthService.InitializeAsync();
        if (!AuthService.IsLoggedIn)
            Navigation.NavigateTo("/login");
    }

    protected void ToggleUsername()
    {
        ShowUsername = !ShowUsername;
        NewUsername = string.Empty;
        UsernameError = UsernameSuccess = null;
    }

    protected void ToggleEmail()
    {
        ShowEmail = !ShowEmail;
        NewEmail = EmailPassword = string.Empty;
        EmailError = EmailSuccess = null;
        ShowEmailPassword = false;
    }

    protected void TogglePassword()
    {
        ShowPassword = !ShowPassword;
        CurrentPassword = NewPassword = NewPasswordConfirm = string.Empty;
        PasswordError = PasswordSuccess = null;
        ShowCurrentPwd = ShowNewPwd = false;
    }

    protected async Task SaveUsername()
    {
        UsernameError = UsernameSuccess = null;
        if (string.IsNullOrWhiteSpace(NewUsername)) { UsernameError = "Inserisci un username."; return; }
        if (NewUsername.Trim().Length < 3) { UsernameError = "Lo username deve contenere almeno 3 caratteri."; return; }
        if (NewUsername.Trim().Length > 32) { UsernameError = "Lo username non può superare 32 caratteri."; return; }

        UsernameLoading = true;
        StateHasChanged();
        try
        {
            var (success, error) = await AuthService.ChangeUsernameAsync(NewUsername.Trim());
            if (success) { UsernameSuccess = "Username aggiornato."; ShowUsername = false; }
            else UsernameError = error;
        }
        finally { UsernameLoading = false; StateHasChanged(); }
    }

    protected async Task SaveEmail()
    {
        EmailError = EmailSuccess = null;
        if (string.IsNullOrWhiteSpace(NewEmail)) { EmailError = "Inserisci la nuova email."; return; }
        if (string.IsNullOrWhiteSpace(EmailPassword)) { EmailError = "Inserisci la password attuale."; return; }

        EmailLoading = true;
        StateHasChanged();
        try
        {
            var (success, error) = await AuthService.ChangeEmailAsync(NewEmail.Trim(), EmailPassword);
            if (success) { EmailSuccess = "Email aggiornata."; ShowEmail = false; }
            else EmailError = error;
        }
        finally { EmailLoading = false; StateHasChanged(); }
    }

    protected async Task SavePassword()
    {
        PasswordError = PasswordSuccess = null;
        if (string.IsNullOrWhiteSpace(CurrentPassword)) { PasswordError = "Inserisci la password attuale."; return; }

        var (valid, policyError) = PasswordPolicy.Validate(NewPassword);
        if (!valid) { PasswordError = policyError; return; }

        if (NewPassword != NewPasswordConfirm) { PasswordError = "Le password non coincidono."; return; }

        PasswordLoading = true;
        StateHasChanged();
        try
        {
            var (success, error) = await AuthService.ChangePasswordAsync(CurrentPassword, NewPassword);
            if (success) { PasswordSuccess = "Password aggiornata."; ShowPassword = false; }
            else PasswordError = error;
        }
        finally { PasswordLoading = false; StateHasChanged(); }
    }

    protected async Task LogoutAsync()
    {
        await AuthService.LogoutAsync();
        Navigation.NavigateTo("/");
    }
}
