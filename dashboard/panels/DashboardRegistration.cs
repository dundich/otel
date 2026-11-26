using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RaMonitor.Panels;

/// <summary>
/// Opt-in self-registration of an app URL in the dashboard host.
/// The app sends its internal URL (for dashboard callbacks) and public URL (for
/// display), then sends a heartbeat every 10 seconds.
/// </summary>
public static class DashboardRegistrationExtensions
{
    public static IHostApplicationBuilder AddDashboardRegistration(this IHostApplicationBuilder builder)
    {
        var registryUrl = builder.Configuration["Dashboard:RegistryUrl"];
        var name = builder.Configuration["Service:Name"];
        var internalUrl = builder.Configuration["Service:InternalUrl"];

        if (string.IsNullOrWhiteSpace(registryUrl) ||
            string.IsNullOrWhiteSpace(name) ||
            string.IsNullOrWhiteSpace(internalUrl))
        {
            return builder;
        }

        var options = new DashboardRegistrationOptions
        {
            RegistryUrl = registryUrl.TrimEnd('/'),
            Name = name,
            InternalUrl = internalUrl.TrimEnd('/'),
            PublicUrl = (builder.Configuration["Service:PublicUrl"] ?? internalUrl).TrimEnd('/'),
            Version = builder.Configuration["Service:Version"]
        };

        builder.Services.AddSingleton(options);
        builder.Services.AddHttpClient("dashboard-registration", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
        });
        builder.Services.AddHostedService<DashboardRegistrationHostedService>();
        return builder;
    }
}

public sealed class DashboardRegistrationOptions
{
    public required string RegistryUrl { get; init; }
    public required string Name { get; init; }
    public required string InternalUrl { get; init; }
    public required string PublicUrl { get; init; }
    public string? Version { get; init; }
}

internal sealed class DashboardRegistrationHostedService : BackgroundService
{
    private readonly DashboardRegistrationOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DashboardRegistrationHostedService> _logger;

    public DashboardRegistrationHostedService(
        DashboardRegistrationOptions options,
        IHttpClientFactory httpClientFactory,
        ILogger<DashboardRegistrationHostedService> logger)
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var id = _options.Name;
        var registered = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("dashboard-registration");
                if (!registered)
                {
                    using var response = await client.PostAsJsonAsync(
                        $"{_options.RegistryUrl}/register",
                        new
                        {
                            id,
                            name = _options.Name,
                            internalUrl = _options.InternalUrl,
                            publicUrl = _options.PublicUrl,
                            version = _options.Version ?? GetVersion()
                        },
                        stoppingToken);

                    registered = response.IsSuccessStatusCode;
                }
                else
                {
                    using var response = await client.PostAsJsonAsync(
                        $"{_options.RegistryUrl}/heartbeat",
                        new { id },
                        stoppingToken);

                    registered = response.IsSuccessStatusCode;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                registered = false;
                _logger.LogDebug(ex, "Dashboard registration attempt failed for {Service}", _options.Name);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        try
        {
            var client = _httpClientFactory.CreateClient("dashboard-registration");
            using var response = await client.DeleteAsync($"{_options.RegistryUrl}/{Uri.EscapeDataString(id)}");
        }
        catch
        {
            // Best-effort; stale entries are removed by the dashboard TTL.
        }
    }

    private static string GetVersion()
    {
        var version = (System.Reflection.Assembly.GetEntryAssembly() ?? System.Reflection.Assembly.GetExecutingAssembly())
            .GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
