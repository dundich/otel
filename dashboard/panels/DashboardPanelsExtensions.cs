using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace RaMonitor.Panels;

public static class DashboardPanelsExtensions
{
    /// <summary>
    /// Registers dashboard panels and their HTTP endpoints. Everything is
    /// configured through the builder; attributes are only sugar via
    /// <see cref="DashboardPanelBuilder.ScanAssembly"/>.
    /// </summary>
    public static IHostApplicationBuilder AddDashboardPanels(
        this IHostApplicationBuilder builder,
        Action<DashboardPanelBuilder> configure,
        string prefix = "/dashboard")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var panels = new DashboardPanelBuilder();
        configure(panels);

        foreach (var registration in panels.Registrations)
        {
            builder.Services.TryAddSingleton(registration.PanelType);
            builder.Services.AddSingleton(registration);
        }

        builder.Services.TryAddSingleton<DashboardPanelRegistry>();
        builder.Services.AddSingleton<IStartupFilter>(
            new DashboardPanelEndpointsStartupFilter("/" + prefix.Trim('/')));

        return builder;
    }

    /// <summary>Attribute sugar: registers all [DashboardPanel] classes in the assembly.</summary>
    public static IHostApplicationBuilder AddDashboardPanels<TMarker>(
        this IHostApplicationBuilder builder,
        string prefix = "/dashboard")
        => builder.AddDashboardPanels(panels => panels.ScanAssembly<TMarker>(), prefix);
}

/// <summary>
/// Serves the two panel routes as middleware registered during host startup.
/// This keeps AddDashboardPanels a single builder call: no app.MapDashboardPanels(),
/// no dependency on endpoint-routing timing.
/// </summary>
internal sealed class DashboardPanelEndpointsStartupFilter(string prefix) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (!context.Request.Path.StartsWithSegments(prefix, out var remaining))
                {
                    await nextMiddleware();
                    return;
                }

                var segments = remaining.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];

                // GET {prefix}/panels
                if (segments is ["panels"])
                {
                    if (!HttpMethods.IsGet(context.Request.Method))
                    {
                        context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                        return;
                    }

                    await WritePanelsAsync(context);
                    return;
                }

                // GET {prefix}/panels/{panelId}
                if (segments is ["panels", var singlePanelId])
                {
                    if (!HttpMethods.IsGet(context.Request.Method))
                    {
                        context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                        return;
                    }

                    await WritePanelAsync(context, singlePanelId);
                    return;
                }

                // POST {prefix}/panels/{panelId}/actions/{actionName}
                if (segments is ["panels", var panelId, "actions", var actionName])
                {
                    if (!HttpMethods.IsPost(context.Request.Method))
                    {
                        context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                        return;
                    }

                    await ExecuteActionAsync(context, panelId, actionName);
                    return;
                }

                await nextMiddleware();
            });

            next(app);
        };

    private static async Task WritePanelsAsync(HttpContext context)
    {
        var registry = context.RequestServices.GetRequiredService<DashboardPanelRegistry>();
        var panels = await registry.GetPanelsAsync(context.RequestAborted);
        await context.Response.WriteAsJsonAsync(panels, context.RequestAborted);
    }

    private static async Task WritePanelAsync(HttpContext context, string panelId)
    {
        var registry = context.RequestServices.GetRequiredService<DashboardPanelRegistry>();
        var panel = await registry.GetPanelAsync(panelId, context.RequestAborted);

        if (panel is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(
                new { error = $"Panel '{panelId}' not found." },
                context.RequestAborted);
            return;
        }

        await context.Response.WriteAsJsonAsync(panel, context.RequestAborted);
    }

    private static async Task ExecuteActionAsync(HttpContext context, string panelId, string actionName)
    {
        var registry = context.RequestServices.GetRequiredService<DashboardPanelRegistry>();

        DashboardActionRequest? request = null;
        if (context.Request.ContentLength is > 0 || context.Request.Headers.TransferEncoding.Count > 0)
        {
            request = await context.Request.ReadFromJsonAsync<DashboardActionRequest>(
                context.RequestAborted);
        }

        var result = await registry.ExecuteAsync(panelId, actionName, request?.Values, context.RequestAborted);

        if (result is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(
                new { error = $"Panel '{panelId}' or action '{actionName}' not found." },
                context.RequestAborted);
            return;
        }

        await context.Response.WriteAsJsonAsync(result, context.RequestAborted);
    }
}
