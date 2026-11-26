using System.Text.Json;

namespace RaMonitor.Panels;

public sealed record DashboardPanelDescriptor(
    string Id,
    string Title,
    string Version,
    string? Description,
    int Order,
    IReadOnlyList<DashboardFieldDescriptor> Fields,
    IReadOnlyList<DashboardActionDescriptor> Actions);

/// <summary>
/// One input in a panel. InputType is one of: text, date, int, number, bool.
/// </summary>
public sealed record DashboardFieldDescriptor(
    string Name,
    string Label,
    string InputType,
    int Order,
    string? Placeholder,
    bool Required,
    object? Value);

public sealed record DashboardActionDescriptor(
    string Name,
    string Label,
    int Order,
    bool RequireConfirm,
    string? Description);

public sealed class DashboardActionRequest
{
    public Dictionary<string, JsonElement> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Content returned by a panel action. <see cref="Type"/> selects the viewer in
/// the dashboard; the default is plain text. New types can be added later
/// without changing the panel contract.
/// </summary>
public sealed record DashboardContent(string Content, string Type = "text")
{
    public static DashboardContent Text(string content) => new(content, "text");
    public static DashboardContent Markdown(string content) => new(content, "markdown");
    public static DashboardContent Json(string content) => new(content, "json");
}

public sealed record DashboardActionResult(
    bool Success,
    DashboardContent Content,
    DashboardPanelDescriptor Panel,
    object? Data = null);
