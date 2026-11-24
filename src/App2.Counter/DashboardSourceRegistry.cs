using System.Net;
using System.Net.Http.Json;
using Dashboard.Client;

namespace App2.Counter;

/// <summary>
/// DI-registered client registry for panels owned by other apps.
/// The dashboard host calls each source through the named "dashboard-sources"
/// HttpClient, so URLs/timeouts/retries are configured in one place.
/// </summary>
public sealed class DashboardSourceRegistry
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DashboardSourceRegistry> _logger;

    public DashboardSourceRegistry(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<DashboardSourceRegistry> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        Sources = configuration.GetSection("Dashboard:Sources").Get<List<DashboardSourceConfig>>() ?? [];
    }

    public IReadOnlyList<DashboardSourceConfig> Sources { get; }

    public async Task<DashboardSourcePanels> GetPanelsAsync(
        DashboardSourceConfig source,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("dashboard-sources");
        var panels = await client.GetFromJsonAsync<List<DashboardPanelDescriptor>>(
            $"{source.Url.TrimEnd('/')}/dashboard/panels",
            cancellationToken) ?? [];

        var uri = new Uri(source.Url);
        return new DashboardSourcePanels(
            source.Name,
            source.PublicUrl,
            source.Url,
            uri.Host,
            await ResolveAsync(uri.Host, cancellationToken),
            panels);
    }

    public static async Task<IReadOnlyList<string>> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
            return addresses.Select(address => address.ToString()).Distinct(StringComparer.Ordinal).ToArray();
        }
        catch
        {
            return [];
        }
    }

    public async Task<DashboardSourceExecution> ExecuteAsync(
        string sourceName,
        string panelId,
        string actionName,
        DashboardActionRequest? request,
        CancellationToken cancellationToken)
    {
        var source = Sources.FirstOrDefault(x =>
            string.Equals(x.Name, sourceName, StringComparison.OrdinalIgnoreCase));

        if (source is null)
        {
            return new DashboardSourceExecution(404, null);
        }

        var url = $"{source.Url.TrimEnd('/')}/dashboard/panels/" +
                  $"{Uri.EscapeDataString(panelId)}/actions/{Uri.EscapeDataString(actionName)}";

        var client = _httpClientFactory.CreateClient("dashboard-sources");
        using var response = await client.PostAsJsonAsync(
            url,
            request ?? new DashboardActionRequest(),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Dashboard source {Source} returned {Status}: {Body}",
                source.Name, (int)response.StatusCode, body);

            return new DashboardSourceExecution((int)response.StatusCode, null);
        }

        var result = await response.Content.ReadFromJsonAsync<DashboardActionResult>(cancellationToken);
        return new DashboardSourceExecution((int)response.StatusCode, result);
    }
}

public sealed class DashboardSourceConfig
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string PublicUrl { get; set; } = string.Empty;
}

public sealed record DashboardSourcePanels(
    string Name,
    string PublicUrl,
    string Url,
    string DnsName,
    IReadOnlyList<string> Addresses,
    IReadOnlyList<DashboardPanelDescriptor> Panels);

public sealed record DashboardSourceExecution(
    int StatusCode,
    DashboardActionResult? Result);
