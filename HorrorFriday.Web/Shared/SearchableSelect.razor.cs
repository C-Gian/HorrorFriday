using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace HorrorFriday.Web.Shared;

public partial class SearchableSelect<TItem> : ComponentBase
{
    [Inject] private IJSRuntime JS { get; set; } = default!;

    [Parameter, EditorRequired] public List<TItem> Options { get; set; } = new();
    [Parameter, EditorRequired] public Func<TItem, string> TextSelector { get; set; } = _ => "";
    [Parameter, EditorRequired] public Func<TItem, string> ValueSelector { get; set; } = _ => "";
    [Parameter] public string Value { get; set; } = "";
    [Parameter] public EventCallback<string> ValueChanged { get; set; }
    [Parameter] public string Placeholder { get; set; } = "Select…";

    protected bool IsOpen { get; set; }
    protected string SearchText { get; set; } = "";
    protected ElementReference SearchInput;

    protected IEnumerable<TItem> FilteredOptions =>
        string.IsNullOrWhiteSpace(SearchText)
            ? Options
            : Options.Where(o => TextSelector(o).Contains(SearchText, StringComparison.OrdinalIgnoreCase));

    protected string GetSelectedText()
    {
        var match = Options.FirstOrDefault(o => ValueSelector(o) == Value);
        return match is not null ? TextSelector(match) : (string.IsNullOrEmpty(Value) ? Placeholder : Value);
    }

    protected async Task Toggle()
    {
        IsOpen = !IsOpen;
        if (IsOpen)
        {
            SearchText = "";
            StateHasChanged();
            await Task.Yield();
            try { await JS.InvokeVoidAsync("focusElement", SearchInput); } catch { }
        }
    }

    protected void Close()
    {
        IsOpen = false;
        SearchText = "";
    }

    protected async Task SelectOption(string value)
    {
        Value = value;
        IsOpen = false;
        SearchText = "";
        await ValueChanged.InvokeAsync(value);
    }
}
