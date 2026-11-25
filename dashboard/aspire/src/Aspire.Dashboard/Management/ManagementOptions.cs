namespace Aspire.Dashboard.Management;

/// <summary>
/// Configuration for the Management page's panel aggregation. Bound from the
/// "Management" configuration section, e.g.:
/// <code>
/// Management__Sources__0__Name=app1
/// Management__Sources__0__Url=http://app1:8080
/// Management__Sources__0__PublicUrl=http://localhost:8081
/// </code>
/// </summary>
public sealed class ManagementOptions
{
    /// <summary>Applications whose /dashboard/panels are shown on the page.</summary>
    public IReadOnlyList<ManagementSource> Sources { get; init; } = [];

    /// <summary>Timeout for a single source request, in seconds.</summary>
    public int TimeoutSeconds { get; init; } = 10;
}

/// <summary>One panel source (an application exposing /dashboard/panels).</summary>
public sealed class ManagementSource
{
    /// <summary>Source name, normally the OTel service name shown in the resource pane.</summary>
    public required string Name { get; init; }

    /// <summary>Internal URL the dashboard uses to reach the application.</summary>
    public required string Url { get; init; }

    /// <summary>Optional public URL shown in the UI. Falls back to <see cref="Url"/>.</summary>
    public string? PublicUrl { get; init; }
}
