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

public sealed record DashboardActionResult(
    bool Success,
    string? Message,
    DashboardPanelDescriptor Panel,
    object? Data = null);
