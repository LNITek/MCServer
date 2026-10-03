using Microsoft.AspNetCore.Components;

namespace MCServer.Plugins;

/// <summary>
/// Lets any page (built-in or plugin-provided) override the <see cref="MainLayout"/>
/// header. If <see cref="Content"/> is set it wins, otherwise <see cref="Title"/>
/// is rendered with the default MudText style. When neither is set the layout
/// falls back to its URL-based default.
/// </summary>
public sealed class PageHeaderService
{
    public event Action? Changed;

    private string? _title;
    private RenderFragment? _content;

    public string? Title => _title;
    public RenderFragment? Content => _content;
    public bool HasCustomHeader => _content is not null || _title is not null;

    /// <summary>Set a plain-text header (rendered with the default MudText).</summary>
    public void SetTitle(string? title)
    {
        _title = title;
        _content = null;
        Changed?.Invoke();
    }

    /// <summary>Set a custom header control/fragment.</summary>
    public void SetHeader(RenderFragment? content)
    {
        _content = content;
        if (content is not null)
            _title = null;
        Changed?.Invoke();
    }

    /// <summary>Clear any custom header, restoring URL-based fallback.</summary>
    public void Clear()
    {
        var had = HasCustomHeader;
        _title = null;
        _content = null;
        if (had)
            Changed?.Invoke();
    }

    /// <summary>
    /// Clear only if the service still holds the given values.
    /// Guards against an old page wiping a newer page's header on dispose
    /// (Blazor can dispose the old page after initializing the new one).
    /// </summary>
    public void ClearIf(string? title, RenderFragment? content)
    {
        if (_title == title && _content == content)
            Clear();
    }
}
