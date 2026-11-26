using Aspire.Dashboard.Model;
using Microsoft.AspNetCore.Components;

namespace Aspire.Dashboard.Components.Content;

/// <summary>
/// Maps a content type to the Blazor component that renders it. The registry is
/// a singleton, so new viewers can be added later without changing panels or
/// the notification center:
/// <code>
/// .ContentViewers(viewers => viewers.Register("html", typeof(HtmlViewer)))
/// </code>
/// Unknown types fall back to the plain text viewer.
/// </summary>
public sealed class ContentViewerRegistry
{
    private readonly Dictionary<string, Type> _viewers = new(StringComparer.OrdinalIgnoreCase);

    public ContentViewerRegistry()
    {
        Register("text", typeof(TextViewer));
        Register("txt", typeof(TextViewer));
        Register("plain", typeof(TextViewer));
        Register("plaintext", typeof(TextViewer));
        Register("json", typeof(JsonViewer));
        Register("markdown", typeof(MarkdownViewer));
        Register("md", typeof(MarkdownViewer));
        Register("yaml", typeof(TextViewer));
        Register("yml", typeof(TextViewer));
    }

    public IReadOnlyDictionary<string, Type> Viewers => _viewers;

    public ContentViewerRegistry Register(string type, Type componentType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(componentType);

        if (!typeof(IComponent).IsAssignableFrom(componentType))
        {
            throw new ArgumentException(
                $"Viewer type '{componentType.FullName}' must be a Blazor component.",
                nameof(componentType));
        }

        _viewers[Normalize(type)] = componentType;
        return this;
    }

    public Type Resolve(string? type) =>
        _viewers.TryGetValue(Normalize(type), out var viewer) ? viewer : typeof(TextViewer);

    public static string Normalize(string? type)
    {
        var value = type?.Trim().ToLowerInvariant();
        return value switch
        {
            null or "" => DashboardContentTypes.Text,
            "txt" or "plain" or "plaintext" or "text/plain" => DashboardContentTypes.Text,
            "md" or "text/markdown" => DashboardContentTypes.Markdown,
            "application/json" => DashboardContentTypes.Json,
            _ => value
        };
    }
}

public static class DashboardContentTypes
{
    public const string Text = "text";
    public const string Markdown = "markdown";
    public const string Json = "json";
}
