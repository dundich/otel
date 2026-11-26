using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Aspire.Dashboard.Components.Content;

namespace Aspire.Dashboard;

/// <summary>
/// Fluent registration of the dashboard as a component of an existing web host.
/// <code>
/// var builder = WebApplication.CreateBuilder(args);
/// builder.AddDashboard(dashboard => dashboard
///     .FromConfiguration(builder.Configuration)
///     .Branding(branding => branding.Title("РА-Монитор"))
///     .Hosting(hosting => hosting.BasePath("/monitor")));
///
/// var app = builder.Build();
/// app.UseDashboard();
/// app.Run();
/// </code>
/// All settings are supplied by the host: domains are grouped into builders, and
/// the dashboard library owns no configuration of its own.
/// </summary>
public static class DashboardHostExtensions
{
    /// <summary>
    /// Registers dashboard services in the host DI container.
    /// </summary>
    public static DashboardWebApplication AddDashboard(
        this WebApplicationBuilder builder,
        Action<DashboardBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var dashboardBuilder = new DashboardBuilder();
        configure?.Invoke(dashboardBuilder);

        var settings = dashboardBuilder.Settings;
        var values = settings.ToConfigurationValues();
        if (values.Count > 0)
        {
            builder.Configuration.AddInMemoryCollection(values);
        }

        var componentOptions = new DashboardComponentOptions
        {
            BasePath = settings.Hosting.BasePath ?? "/",
            PublicUrl = settings.Hosting.PublicUrl,
            Title = settings.Branding.Title,
            LogoUrl = settings.Branding.LogoUrl
        };

        // The dashboard also needs static web assets outside Development when it
        // runs from source (e.g. _content/ from Fluent UI). No-op when published.
        if (!builder.Environment.IsDevelopment())
        {
            builder.WebHost.UseStaticWebAssets();
        }

        // Reuse the same host builder for host-specific registrations, such as
        // the existing panel SDK (AddDashboardPanels).
        foreach (var configureHost in dashboardBuilder.HostRegistrations)
        {
            configureHost(builder);
        }

        // The registry is created before the dashboard services so the host's
        // viewer registrations win over the default TryAddSingleton.
        var contentViewers = new ContentViewerRegistry();
        foreach (var configureViewers in dashboardBuilder.ContentViewerRegistrations)
        {
            configureViewers(contentViewers);
        }
        builder.Services.AddSingleton(contentViewers);

        var dashboard = new DashboardWebApplication(builder, componentOptions);

        builder.Services.AddSingleton(componentOptions);
        builder.Services.AddSingleton(dashboard);

        return dashboard;
    }

    /// <summary>
    /// Adds dashboard middleware and endpoints to a built host.
    /// </summary>
    public static WebApplication UseDashboard(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.Services.GetRequiredService<DashboardWebApplication>().Use(app);
        return app;
    }
}
