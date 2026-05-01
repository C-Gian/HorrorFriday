using HorrorFriday.Web.Helpers;
using HorrorFriday.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace HorrorFriday.Web.Pages;

public partial class ResetPassword : ComponentBase
{
    [Inject] private AuthService AuthService { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "token")]
    public string? Token { get; set; }

    protected string Password { get; set; } = string.Empty;
    protected string PasswordConfirm { get; set; } = string.Empty;
    protected string? ErrorMessage { get; set; }
    protected bool IsLoading { get; set; }
    protected bool Done { get; set; }
    protected bool InvalidToken { get; set; }
    protected bool ShowPassword { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await AuthService.InitializeAsync();
        if (AuthService.IsLoggedIn)
            Navigation.NavigateTo("/");

        if (string.IsNullOrWhiteSpace(Token))
            InvalidToken = true;
    }

    protected async Task Submit()
    {
        ErrorMessage = null;

        var (valid, policyError) = PasswordPolicy.Validate(Password);
        if (!valid) { ErrorMessage = policyError; return; }

        if (Password != PasswordConfirm)
        {
            ErrorMessage = "Le password non coincidono.";
            return;
        }

        IsLoading = true;
        StateHasChanged();
        try
        {
            var (success, error) = await AuthService.ResetPasswordAsync(Token!, Password);
            if (success)
                Done = true;
            else
            {
                // Token invalid/expired
                InvalidToken = true;
                ErrorMessage = error;
            }
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
