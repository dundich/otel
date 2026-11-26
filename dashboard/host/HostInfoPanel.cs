using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using RaMonitor.Panels;

namespace RaMonitor.Host;

/// <summary>
/// Small example of a panel owned by the dashboard host itself. It is served at
/// /dashboard/panels and registered through the existing panels SDK.
/// </summary>
[DashboardPanel("Хост", order: 100, Id = "host",
    Description = "Версия и среда dashboard-хоста")]
public sealed class HostInfoPanel(IWebHostEnvironment environment) : IDashboardPanel
{
    [DashboardInput("Версия", DashboardInputType.ReadOnly, sortOrder: 1)]
    public string Version { get; private set; } = string.Empty;

    [DashboardInput("Среда", DashboardInputType.ReadOnly, sortOrder: 2)]
    public string Environment { get; private set; } = string.Empty;

    [DashboardInput("Сейчас", DashboardInputType.ReadOnly, sortOrder: 3)]
    public string Now { get; private set; } = string.Empty;

    public Task RenderAsync(CancellationToken cancellationToken = default)
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(HostInfoPanel).Assembly;
        Version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";
        Environment = environment.EnvironmentName;
        Now = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");

        return Task.CompletedTask;
    }
}
