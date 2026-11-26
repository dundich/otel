namespace Aspire.Dashboard.Model;

/// <summary>
/// Content sent by an app panel action. <see cref="Type"/> selects a viewer
/// from <c>ContentViewerRegistry</c>; the default is plain text.
/// </summary>
public sealed record DashboardContent(string Content, string Type = "text");
