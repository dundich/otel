using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dashboard.Client;

public static class DashboardPanelsExtensions
{
    /// <summary>
    /// Explicitly registers one panel type in DI. This is the recommended way when
    /// the app wants full control over panel lifetime and constructor dependencies.
    /// </summary>
    public static IServiceCollection AddDashboardPanel<TPanel>(this IServiceCollection services)
        where TPanel : class
    {
        services.TryAddSingleton<TPanel>();
        services.AddSingleton(new DashboardPanelRegistration(typeof(TPanel)));
        services.TryAddSingleton<DashboardPanelRegistry>();
        return services;
    }

    /// <summary>
    /// Scans the given assemblies for [DashboardPanel] classes, registers them as
    /// singletons and adds the registry used by the HTTP endpoints.
    /// </summary>
    public static IServiceCollection AddDashboardPanels(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        var scanned = (assemblies.Length == 0 ? new[] { Assembly.GetCallingAssembly() } : assemblies)
            .Distinct()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } &&
                           type.GetCustomAttribute<DashboardPanelAttribute>() is not null)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        foreach (var type in scanned)
        {
            if (services.Any(descriptor => descriptor.ServiceType == type))
            {
                continue;
            }

            services.AddSingleton(type);
            services.AddSingleton(new DashboardPanelRegistration(type));
        }

        services.TryAddSingleton<DashboardPanelRegistry>();
        return services;
    }

    /// <summary>
    /// Exposes GET {prefix}/panels and POST {prefix}/panels/{panelId}/actions/{actionName}.
    /// </summary>
    public static IEndpointRouteBuilder MapDashboardPanels(
        this IEndpointRouteBuilder endpoints,
        string prefix = "/dashboard")
    {
        prefix = "/" + prefix.Trim('/');

        endpoints.MapGet($"{prefix}/panels", async (
            DashboardPanelRegistry registry,
            CancellationToken cancellationToken) =>
        {
            var panels = await registry.GetPanelsAsync(cancellationToken);
            return Results.Ok(panels);
        });

        endpoints.MapPost($"{prefix}/panels/{{panelId}}/actions/{{actionName}}", async (
            string panelId,
            string actionName,
            DashboardActionRequest? request,
            DashboardPanelRegistry registry,
            CancellationToken cancellationToken) =>
        {
            var result = await registry.ExecuteAsync(panelId, actionName, request?.Values, cancellationToken);

            return result is null
                ? Results.NotFound(new { error = $"Panel '{panelId}' or action '{actionName}' not found." })
                : Results.Ok(result);
        });

        return endpoints;
    }
}
