namespace Aspire.Dashboard.Management;

/// <summary>Registers the Management page's panel aggregation services.</summary>
public static class ManagementServiceCollectionExtensions
{
    public static IServiceCollection AddDashboardManagement(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("Management");
        var options = new ManagementOptions
        {
            TimeoutSeconds = int.TryParse(section["TimeoutSeconds"], out var timeout) && timeout > 0 ? timeout : 10,
            Sources = section.GetSection("Sources").GetChildren()
                .Select(child => new ManagementSource
                {
                    Name = child["Name"] ?? string.Empty,
                    Url = child["Url"] ?? string.Empty,
                    PublicUrl = child["PublicUrl"]
                })
                .Where(source => !string.IsNullOrWhiteSpace(source.Name) && !string.IsNullOrWhiteSpace(source.Url))
                .ToArray()
        };

        services.AddSingleton(options);
        services.AddHttpClient(ManagementPanelService.HttpClientName)
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds));
        services.AddSingleton<ManagementPanelService>();

        return services;
    }
}
