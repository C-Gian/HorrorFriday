using Microsoft.AspNetCore.Components;

namespace HorrorFriday.Web.Shared;

public partial class CustomSelect<TItem> : ComponentBase
{
    [Parameter, EditorRequired]
    public List<TItem> Options { get; set; } = new();

    [Parameter, EditorRequired]
    public Func<TItem, string> TextSelector { get; set; } = _ => "";

    [Parameter, EditorRequired]
    public Func<TItem, string> ValueSelector { get; set; } = _ => "";

    [Parameter]
    public string Value { get; set; } = "";

    [Parameter]
    public EventCallback<string> ValueChanged { get; set; }

    protected bool IsOpen { get; set; }

    protected string GetSelectedText()
    {
        var match = Options.FirstOrDefault(o => ValueSelector(o) == Value);
        return match is not null ? TextSelector(match) : Value;
    }

    protected void Toggle()
    {
        IsOpen = !IsOpen;
    }

    protected void Close()
    {
        IsOpen = false;
    }

    protected async Task SelectOption(string value)
    {
        Value = value;
        IsOpen = false;
        await ValueChanged.InvokeAsync(value);
    }
}