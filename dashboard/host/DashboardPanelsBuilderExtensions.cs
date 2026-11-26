using Aspire.Dashboard;
using RaMonitor.Panels;

namespace RaMonitor.Host;

/// <summary>
/// Adds the existing panels SDK to the dashboard host DI. The method is an
/// extension on <see cref="DashboardBuilder"/>, so it stays inside the
/// AddDashboard chain:
/// <code>
/// builder.AddDashboard(dashboard => dashboard
///     .Panels(panels => panels.ScanAssembly&lt;HostInfoPanel&gt;()));
/// </code>
/// </summary>
internal static class DashboardPanelsBuilderExtensions
{
    public static DashboardBuilder Panels(
        this DashboardBuilder dashboard,
        Action<DashboardPanelBuilder> configure,
        string prefix = "/dashboard")
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        ArgumentNullException.ThrowIfNull(configure);

        return dashboard.ConfigureHost(host => host.AddDashboardPanels(configure, prefix));
    }
}
