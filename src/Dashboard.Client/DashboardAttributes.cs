namespace Dashboard.Client;

/// <summary>
/// Optional sugar for builder registration: marks a class as a dashboard panel
/// when the assembly is scanned via <c>AddDashboardPanels&lt;TMarker&gt;()</c> or
/// <c>DashboardPanelBuilder.ScanAssembly(...)</c>. Explicit
/// <c>DashboardPanelBuilder.Panel&lt;T&gt;(...)</c> doesn't need it.
/// Each panel is rendered as an accordion in the dashboard UI and registered as
/// a singleton in DI.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DashboardPanelAttribute(string title, int order = 0) : Attribute
{
    public string Title { get; } = title;
    public int Order { get; } = order;

    /// <summary>Stable panel id. Defaults to the class name.</summary>
    public string? Id { get; init; }

    public string? Description { get; init; }
}

public enum DashboardInputType
{
    Auto = 0,
    Text,
    Date,
    Int,
    Number,
    Bool
}

/// <summary>
/// Marks a public property or field as an input in the dashboard panel.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class DashboardInputAttribute(
    string label,
    DashboardInputType type = DashboardInputType.Auto,
    int sortOrder = 0) : Attribute
{
    public string Label { get; } = label;
    public DashboardInputType Type { get; } = type;
    public int SortOrder { get; } = sortOrder;
    public string? Placeholder { get; init; }
    public bool Required { get; init; }
}

/// <summary>
/// Marks a public parameterless method as a button in the dashboard panel.
/// Before the method is called, the SDK writes the submitted input values
/// into the panel object's properties/fields.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class DashboardActionAttribute(string label, int sortOrder = 0) : Attribute
{
    public string Label { get; } = label;
    public int SortOrder { get; } = sortOrder;
    public bool RequireConfirm { get; init; }
    public string? Description { get; init; }
}
