using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace HorrorFriday.Web.Shared;

public partial class CookieBanner : ComponentBase
{
    [Inject] private IJSRuntime JS { get; set; } = default!;

    protected bool Visible { get; set; }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        var consent = await JS.InvokeAsync<string?>("localStorage.getItem", "hf_cookie_consent");
        if (consent != "true")
        {
            Visible = true;
            StateHasChanged();
        }
    }

    protected async Task Accept()
    {
        await JS.InvokeVoidAsync("localStorage.setItem", "hf_cookie_consent", "true");
        Visible = false;
    }
}
