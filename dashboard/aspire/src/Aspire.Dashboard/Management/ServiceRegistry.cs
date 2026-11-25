using System.Collections.Concurrent;

namespace Aspire.Dashboard.Management;

/// <summary>
/// In-memory service registry for self-registration. Apps POST their internal URL
/// (used by the dashboard for callbacks) and public URL (shown in the UI), then
/// heartbeat every 10 seconds. Replace with a shared store for multi-replica dashboards.
/// </summary>
public sealed class ServiceRegistry
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(45);

    private readonly ConcurrentDictionary<string, RegisteredService> _services = new(StringComparer.OrdinalIgnoreCase);

    public void Register(RegisterServiceRequest request, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new InvalidOperationException("Service name is required.");
        }

        var internalUrl = NormalizeUrl(request.InternalUrl);
        var publicUrl = string.IsNullOrWhiteSpace(request.PublicUrl) ? internalUrl : NormalizeUrl(request.PublicUrl);
        var id = string.IsNullOrWhiteSpace(request.Id) ? request.Name : request.Id;

        _services[id] = new RegisteredService(
            Id: id,
            Name: request.Name,
            InternalUrl: internalUrl,
            PublicUrl: publicUrl,
            Version: request.Version ?? string.Empty,
            LastSeen: now);
    }

    public bool Heartbeat(string id, DateTimeOffset now)
    {
        if (!_services.TryGetValue(id, out var service))
        {
            return false;
        }

        _services[id] = service with { LastSeen = now };
        return true;
    }

    public bool Remove(string id) => _services.TryRemove(id, out _);

    public IReadOnlyList<RegisteredService> GetAll(DateTimeOffset now, TimeSpan? ttl = null)
    {
        var cutoff = now - (ttl ?? DefaultTtl);

        foreach (var pair in _services)
        {
            if (pair.Value.LastSeen < cutoff)
            {
                _services.TryRemove(pair.Key, out _);
            }
        }

        return _services.Values
            .OrderBy(service => service.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public RegisteredService? Get(string nameOrId, DateTimeOffset now, TimeSpan? ttl = null)
    {
        return GetAll(now, ttl).FirstOrDefault(service =>
            string.Equals(service.Name, nameOrId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(service.Id, nameOrId, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException($"Invalid absolute http(s) URL: {value}");
        }

        return value.TrimEnd('/');
    }
}

public sealed record RegisteredService(
    string Id,
    string Name,
    string InternalUrl,
    string PublicUrl,
    string Version,
    DateTimeOffset LastSeen);

public sealed class RegisterServiceRequest
{
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string InternalUrl { get; set; } = string.Empty;
    public string? PublicUrl { get; set; }
    public string? Version { get; set; }
}

public sealed class HeartbeatRequest
{
    public string Id { get; set; } = string.Empty;
}
