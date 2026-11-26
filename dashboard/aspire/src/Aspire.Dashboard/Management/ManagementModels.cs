using System.Text.Json;
using Aspire.Dashboard.Model;

namespace Aspire.Dashboard.Management;

/// <summary>Aggregated response of GET /api/panels equivalents across all sources.</summary>
public sealed class PanelsResponse
{
    public List<PanelSourceDto> Sources { get; set; } = [];
    public List<string> Errors { get; set; } = [];
}

/// <summary>Panels exposed by a single application.</summary>
public sealed class PanelSourceDto
{
    public string Name { get; set; } = string.Empty;
    public string PublicUrl { get; set; } = string.Empty;
    public List<PanelDto> Panels { get; set; } = [];
}

public sealed class PanelDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Version { get; set; } = "0.0.0";
    public string? Description { get; set; }
    public string Kind { get; set; } = "panel";
    public List<FieldDto> Fields { get; set; } = [];
    public List<ActionDto> Actions { get; set; } = [];
    public List<ElementDto> Elements { get; set; } = [];
}

/// <summary>Static label or alert inside a panel body.</summary>
public sealed class ElementDto
{
    public string Type { get; set; } = "label";
    public string Text { get; set; } = string.Empty;
    public int Order { get; set; }
}

public sealed class FieldDto
{
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string InputType { get; set; } = "text";
    public string? Placeholder { get; set; }
    public bool Required { get; set; }
    public JsonElement Value { get; set; }
}

public sealed class ActionDto
{
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool RequireConfirm { get; set; }
    public string? Description { get; set; }
}

/// <summary>Response of POST /dashboard/panels/{id}/actions/{name}.</summary>
public sealed class ActionResultDto
{
    public bool Success { get; set; }
    public ContentDto? Content { get; set; }

    /// <summary>Legacy text response from older panel SDK versions.</summary>
    public string? Message { get; set; }

    public PanelDto? Panel { get; set; }

    public DashboardContent? ToDashboardContent()
    {
        if (Content is not null)
        {
            return new DashboardContent(Content.Content, Content.Type);
        }

        return Message is null ? null : new DashboardContent(Message);
    }
}

/// <summary>Content payload returned by a panel action.</summary>
public sealed class ContentDto
{
    public string Content { get; set; } = string.Empty;
    public string Type { get; set; } = "text";
}

/// <summary>
/// Transport-level result of an action call: whether the source answered with 2xx,
/// the raw body (for error reporting) and the parsed payload if it was JSON.
/// </summary>
public sealed record ActionOutcome(bool IsSuccessStatusCode, int StatusCode, string Body, ActionResultDto? Result);
