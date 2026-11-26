using System.Text.Json;

namespace RaMonitor.Panels;

/// <summary>Where a panel is shown in the dashboard.</summary>
public enum DashboardPanelKind
{
    /// <summary>Regular panel on the «Управление» tab.</summary>
    Panel = 0,

    /// <summary>Search panel with a filter toolbar on the «Поиск» tab.</summary>
    Search = 1
}

public static class DashboardPanelKinds
{
    public const string Panel = "panel";
    public const string Search = "search";

    public static string ToWireValue(DashboardPanelKind kind) =>
        kind == DashboardPanelKind.Search ? Search : Panel;
}

public static class DashboardElementTypes
{
    public const string Label = "label";
    public const string Note = "note";
    public const string Warn = "warn";
    public const string Info = "info";
}

public sealed record DashboardPanelDescriptor(
    string Id,
    string Title,
    string Version,
    string? Description,
    int Order,
    IReadOnlyList<DashboardFieldDescriptor> Fields,
    IReadOnlyList<DashboardActionDescriptor> Actions,
    IReadOnlyList<DashboardElementDescriptor> Elements,
    string Kind = DashboardPanelKinds.Panel,
    int? RefreshIntervalMs = null);

/// <summary>
/// Static element inside a panel body: plain label or an alert with information.
/// <see cref="Type"/> is one of label, note, warn, info.
/// </summary>
public sealed record DashboardElementDescriptor(
    string Type,
    string Text,
    int Order);

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
