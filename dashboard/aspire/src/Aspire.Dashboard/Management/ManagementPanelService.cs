using System.Net.Http.Json;
using System.Text.Json;

namespace Aspire.Dashboard.Management;

/// <summary>
/// Aggregates panels from all configured sources. The dashboard is the one who
/// knows the source list (Management:Sources), so the page talks to each
/// application directly — no intermediate aggregator service.
/// </summary>
public sealed class ManagementPanelService
{
    public const string HttpClientName = "management-panels";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ManagementOptions _options;
    private readonly ServiceRegistry _registry;
    private readonly ILogger<ManagementPanelService> _logger;

    public ManagementPanelService(
        IHttpClientFactory httpClientFactory,
        ManagementOptions options,
        ServiceRegistry registry,
        ILogger<ManagementPanelService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _registry = registry;
        _logger = logger;
    }

    public IReadOnlyList<ManagementSource> Sources => GetSources();

    private IReadOnlyList<ManagementSource> GetSources()
    {
        var registered = _registry.GetAll(DateTimeOffset.UtcNow)
            .Select(service => new ManagementSource
            {
                Name = service.Name,
                Url = service.InternalUrl,
                PublicUrl = service.PublicUrl
            })
            .ToArray();

        // Registry is the normal path. Management:Sources remains a static fallback
        // for local runs where self-registration is disabled.
        return registered.Length > 0 ? registered : _options.Sources;
    }

    /// <summary>
    /// Fetches panels from every configured source. A failed source does not
    /// fail the whole page: it lands in <see cref="PanelsResponse.Errors"/>.
    /// </summary>
    public async Task<PanelsResponse> GetPanelsAsync(CancellationToken cancellationToken = default)
    {
        var response = new PanelsResponse();

        foreach (var source in GetSources())
        {
            try
            {
                response.Sources.Add(await GetSourceAsync(source, cancellationToken));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Management source {Source} is unavailable", source.Name);
                response.Errors.Add($"{source.Name}: {ex.Message}");
            }
        }

        return response;
    }

    /// <summary>
    /// Posts an action to the application that owns the panel and returns the
    /// transport result. Parsing/validation is left to the UI.
    /// </summary>
    public async Task<ActionOutcome> ExecuteActionAsync(
        string sourceName,
        string panelId,
        string actionName,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
    {
        var source = GetSources().FirstOrDefault(x =>
            string.Equals(x.Name, sourceName, StringComparison.OrdinalIgnoreCase));

        if (source is null)
        {
            return new ActionOutcome(false, 404, $"Unknown management source '{sourceName}'.", null);
        }

        var client = _httpClientFactory.CreateClient(HttpClientName);
        var url = $"{source.Url.TrimEnd('/')}/dashboard/panels/" +
                  $"{Uri.EscapeDataString(panelId)}/actions/{Uri.EscapeDataString(actionName)}";

        using var response = await client.PostAsJsonAsync(url, new { values }, JsonOptions, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        ActionResultDto? result = null;
        try
        {
            result = JsonSerializer.Deserialize<ActionResultDto>(body, JsonOptions);
        }
        catch (JsonException)
        {
            // Keep the raw body for the status line.
        }

        return new ActionOutcome(response.IsSuccessStatusCode, (int)response.StatusCode, body, result);
    }

    /// <summary>
    /// Re-reads one panel descriptor. Used by the optional auto-refresh timer.
    /// </summary>
    public async Task<PanelDto?> GetPanelAsync(
        string sourceName,
        string panelId,
        CancellationToken cancellationToken = default)
    {
        var source = GetSources().FirstOrDefault(x =>
            string.Equals(x.Name, sourceName, StringComparison.OrdinalIgnoreCase));

        if (source is null)
        {
            return null;
        }

        var client = _httpClientFactory.CreateClient(HttpClientName);
        return await client.GetFromJsonAsync<PanelDto>(
            $"{source.Url.TrimEnd('/')}/dashboard/panels/{Uri.EscapeDataString(panelId)}",
            JsonOptions,
            cancellationToken);
    }

    private async Task<PanelSourceDto> GetSourceAsync(ManagementSource source, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var panels = await client.GetFromJsonAsync<List<PanelDto>>(
            $"{source.Url.TrimEnd('/')}/dashboard/panels",
            JsonOptions,
            cancellationToken) ?? [];

        return new PanelSourceDto
        {
            Name = source.Name,
            PublicUrl = string.IsNullOrWhiteSpace(source.PublicUrl) ? source.Url : source.PublicUrl,
            Panels = panels
        };
    }
}
