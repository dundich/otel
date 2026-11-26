using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Aspire.Dashboard.Components.Content;

namespace Aspire.Dashboard;

/// <summary>
/// Fluent, domain-oriented configuration for the dashboard component:
/// <code>
/// builder.AddDashboard(dashboard => dashboard
///     .Branding(branding => branding
///         .Title("РА-Монитор")
///         .LogoUrl("/img/logo-ra.svg"))
///     .Hosting(hosting => hosting
///         .BasePath("/monitor")
///         .PublicUrl("https://example.com/monitor"))
///     .Files(files => files
///         .Title("Файлы")
///         .Root("logs", "/workspace/logs", "Логи", "/files/logs")
///         .Root("share", "/host-home", "Share", "/files/share"))
///     .Otlp(otlp => otlp
///         .GrpcEndpoint("http://0.0.0.0:18889")
///         .Unsecured())
///     .Management(management => management
///         .Timeout(10)));
/// </code>
/// <see cref="FromConfiguration(IConfiguration)"/> binds the same domains from
/// the host configuration in one call; individual builders can still override.
/// </summary>
public sealed class DashboardBuilder
{
    internal DashboardSettings Settings { get; } = new();

    /// <summary>
    /// Host-specific registration hooks. Applied while <c>AddDashboard</c> is
    /// building the host, so extensions can reuse the same DI container.
    /// </summary>
    internal List<Action<IHostApplicationBuilder>> HostRegistrations { get; } = [];

    /// <summary>
    /// Extra content viewers registered by the host. Default text, JSON and
    /// Markdown viewers are always available.
    /// </summary>
    internal List<Action<ContentViewerRegistry>> ContentViewerRegistrations { get; } = [];

    /// <summary>
    /// Bootstraps all domains from the host configuration. Values are read from
    /// the same keys the dashboard has always used, so existing appsettings and
    /// environment variables keep working.
    /// </summary>
    public DashboardBuilder FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var branding = Settings.Branding;
        branding.Title = configuration["Dashboard:Branding:Title"]
            ?? configuration["Dashboard:ApplicationName"]
            ?? branding.Title;
        branding.LogoUrl = configuration["Dashboard:Branding:LogoUrl"] ?? branding.LogoUrl;

        var hosting = Settings.Hosting;
        hosting.BasePath = configuration["Dashboard:BasePath"] ?? hosting.BasePath;
        hosting.PublicUrl = configuration["Dashboard:Frontend:PublicUrl"] ?? hosting.PublicUrl;
        hosting.EndpointUrls = configuration["ASPNETCORE_URLS"] ?? configuration["urls"] ?? hosting.EndpointUrls;

        var files = Settings.Files;
        files.Title = configuration["Dashboard:Files:Title"] ?? files.Title;

        foreach (var root in configuration.GetSection("Dashboard:Files:Roots").GetChildren())
        {
            var id = root.Key;
            var path = root["Path"];
            if (!string.IsNullOrWhiteSpace(path))
            {
                files.SetRoot(id, path, root["Title"], root["Url"]);
            }
        }

        // Backwards compatibility with the original two-tab settings.
        if (configuration["Dashboard:Files:Logs:Path"] is { Length: > 0 } logsPath)
        {
            files.SetRoot("logs", logsPath,
                configuration["Dashboard:Files:Logs:Title"] ?? "Логи",
                configuration["Dashboard:Files:Logs:Url"] ?? "/files/logs");
        }
        if (configuration["Dashboard:Files:Share:Path"] is { Length: > 0 } sharePath)
        {
            files.SetRoot("share", sharePath,
                configuration["Dashboard:Files:Share:Title"] ?? "Share",
                configuration["Dashboard:Files:Share:Url"] ?? "/files/share");
        }

        var otlp = Settings.Otlp;
        otlp.GrpcEndpointUrl = configuration["ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL"]
            ?? configuration["Dashboard:Otlp:GrpcEndpointUrl"]
            ?? otlp.GrpcEndpointUrl;
        otlp.HttpEndpointUrl = configuration["ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL"]
            ?? configuration["Dashboard:Otlp:HttpEndpointUrl"]
            ?? otlp.HttpEndpointUrl;
        otlp.SuppressUnsecuredMessage = configuration.GetValue<bool?>(
            "Dashboard:Otlp:SuppressUnsecuredMessage") ?? otlp.SuppressUnsecuredMessage;

        var security = Settings.Security;
        security.AllowAnonymous = configuration.GetValue<bool?>(
            "ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS")
            ?? configuration.GetValue<bool?>("Dashboard:UnsecuredAllowAnonymous")
            ?? security.AllowAnonymous;

        var management = Settings.Management;
        management.TimeoutSeconds = configuration.GetValue<int?>(
            "Management:TimeoutSeconds") ?? management.TimeoutSeconds;

        foreach (var source in configuration.GetSection("Management:Sources").GetChildren())
        {
            var name = source["Name"];
            var url = source["Url"];
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(url))
            {
                management.AddSource(name, url, source["PublicUrl"]);
            }
        }

        return this;
    }

    /// <summary>Application name, page title and logo shown by the host.</summary>
    public DashboardBuilder Branding(Action<DashboardBrandingBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(new DashboardBrandingBuilder(Settings.Branding));
        return this;
    }

    /// <summary>Public URL, endpoints and mount path of this host.</summary>
    public DashboardBuilder Hosting(Action<DashboardHostingBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(new DashboardHostingBuilder(Settings.Hosting));
        return this;
    }

    /// <summary>File browser roots shown in the single "Files" tab.</summary>
    public DashboardBuilder Files(Action<DashboardFilesBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(new DashboardFilesBuilder(Settings.Files));
        return this;
    }

    /// <summary>OTLP ingestion endpoints.</summary>
    public DashboardBuilder Otlp(Action<DashboardOtlpBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(new DashboardOtlpBuilder(Settings.Otlp));
        return this;
    }

    /// <summary>Management page: panel sources and request timeout.</summary>
    public DashboardBuilder Management(Action<DashboardManagementBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(new DashboardManagementBuilder(Settings.Management));
        return this;
    }

    /// <summary>
    /// Escape hatch for host-specific registrations. The callback runs during
    /// <c>AddDashboard</c> with the same <see cref="IHostApplicationBuilder"/>
    /// used by the dashboard, for example to add the panels SDK.
    /// </summary>
    public DashboardBuilder ConfigureHost(Action<IHostApplicationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        HostRegistrations.Add(configure);
        return this;
    }

    /// <summary>
    /// Adds or replaces content viewers. The registry maps a content type, such
    /// as "markdown" or "json", to a Blazor component. Register new viewers here
    /// as the set of supported content types grows.
    /// </summary>
    public DashboardBuilder ContentViewers(Action<ContentViewerRegistry> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        ContentViewerRegistrations.Add(configure);
        return this;
    }

    /// <summary>Step-1 security switches.</summary>
    public DashboardBuilder Security(Action<DashboardSecurityBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(new DashboardSecurityBuilder(Settings.Security));
        return this;
    }
}

public sealed class DashboardBrandingBuilder
{
    private readonly DashboardBrandingSettings _settings;

    internal DashboardBrandingBuilder(DashboardBrandingSettings settings) => _settings = settings;
    public DashboardBrandingBuilder Title(string? title)
    {
        _settings.Title = title;
        return this;
    }

    public DashboardBrandingBuilder LogoUrl(string? logoUrl)
    {
        _settings.LogoUrl = string.IsNullOrWhiteSpace(logoUrl)
            ? DashboardBrandingSettings.DefaultLogoUrl
            : logoUrl;
        return this;
    }
}

public sealed class DashboardHostingBuilder
{
    private readonly DashboardHostingSettings _settings;

    internal DashboardHostingBuilder(DashboardHostingSettings settings) => _settings = settings;
    public DashboardHostingBuilder BasePath(string? basePath)
    {
        _settings.BasePath = basePath;
        return this;
    }

    public DashboardHostingBuilder PublicUrl(string? publicUrl)
    {
        _settings.PublicUrl = publicUrl;
        return this;
    }

    /// <summary>Frontend listen URLs, e.g. "http://0.0.0.0:18888".</summary>
    public DashboardHostingBuilder EndpointUrls(string? endpointUrls)
    {
        _settings.EndpointUrls = endpointUrls;
        return this;
    }
}

public sealed class DashboardFilesBuilder
{
    private readonly DashboardFilesSettings _settings;

    internal DashboardFilesBuilder(DashboardFilesSettings settings) => _settings = settings;

    /// <summary>Title of the single files tab, for example "Файлы".</summary>
    public DashboardFilesBuilder Title(string? title)
    {
        _settings.Title = title;
        return this;
    }

    /// <summary>
    /// Adds a selectable file root and configures it through a child builder:
    /// <code>
    /// .Root("logs", root => root
    ///     .Title("Логи")
    ///     .Path("/workspace/logs")
    ///     .Url("/files/logs"))
    /// </code>
    /// </summary>
    public DashboardFilesBuilder Root(string id, Action<DashboardFileRootBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(new DashboardFileRootBuilder(_settings, id));
        return this;
    }

    /// <summary>
    /// Adds or configures every root from the Dashboard:Files:Roots configuration
    /// section through the child builders.
    /// </summary>
    public DashboardFilesBuilder Roots(Action<DashboardFilesRootsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(new DashboardFilesRootsBuilder(_settings));
        return this;
    }

    /// <summary>
    /// Adds a selectable file root. <paramref name="url"/> is an app-relative
    /// URL under /files, for example "/files/logs". A null path removes the root.
    /// </summary>
    public DashboardFilesBuilder Root(string id, string? path, string? title = null, string? url = null)
    {
        _settings.SetRoot(id, path, title, url);
        return this;
    }

    public DashboardFilesBuilder Logs(string? path, string? title = null, string? url = null)
        => Root("logs", path, title ?? "Логи", url);

    public DashboardFilesBuilder Share(string? path, string? title = null, string? url = null)
        => Root("share", path, title ?? "Share", url);
}

public sealed class DashboardFilesRootsBuilder
{
    private readonly DashboardFilesSettings _settings;

    internal DashboardFilesRootsBuilder(DashboardFilesSettings settings) => _settings = settings;

    /// <summary>Adds or configures one root through its own builder.</summary>
    public DashboardFilesRootsBuilder Root(string id, Action<DashboardFileRootBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(new DashboardFileRootBuilder(_settings, id));
        return this;
    }
}

public sealed class DashboardFileRootBuilder
{
    private readonly DashboardFilesSettings _settings;
    private readonly string _id;
    private string? _title;
    private string? _path;
    private string? _url;

    internal DashboardFileRootBuilder(DashboardFilesSettings settings, string id)
    {
        _settings = settings;
        _id = id;

        if (settings.GetRoot(id) is { } existing)
        {
            _title = existing.Title;
            _path = existing.Path;
            _url = existing.Url;
        }
    }

    /// <summary>Combo item label of the root.</summary>
    public DashboardFileRootBuilder Title(string? title)
    {
        _title = title;
        return Apply();
    }

    /// <summary>Filesystem root shown by the browser.</summary>
    public DashboardFileRootBuilder Path(string? path)
    {
        _path = path;
        return Apply();
    }

    /// <summary>App-relative URL under /files, for example "/files/logs".</summary>
    public DashboardFileRootBuilder Url(string? url)
    {
        _url = url;
        return Apply();
    }

    /// <summary>Binds this root from Dashboard:Files:Roots:{id}.</summary>
    public DashboardFileRootBuilder FromConfiguration(IConfiguration configuration, string? sectionPath = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(sectionPath ?? $"Dashboard:Files:Roots:{_id}");
        _title = section["Title"] ?? _title;
        _path = section["Path"] ?? _path;
        _url = section["Url"] ?? _url;
        return Apply();
    }

    private DashboardFileRootBuilder Apply()
    {
        _settings.SetRoot(_id, _path, _title, _url);
        return this;
    }
}

public sealed class DashboardOtlpBuilder
{
    private readonly DashboardOtlpSettings _settings;

    internal DashboardOtlpBuilder(DashboardOtlpSettings settings) => _settings = settings;
    /// <summary>OTLP/gRPC endpoint URL, e.g. "http://0.0.0.0:18889".</summary>
    public DashboardOtlpBuilder GrpcEndpoint(string? endpointUrl)
    {
        _settings.GrpcEndpointUrl = endpointUrl;
        return this;
    }

    public DashboardOtlpBuilder HttpEndpoint(string? endpointUrl)
    {
        _settings.HttpEndpointUrl = endpointUrl;
        return this;
    }

    public DashboardOtlpBuilder Unsecured(bool unsecured = true)
    {
        _settings.Unsecured = unsecured;
        return this;
    }

    public DashboardOtlpBuilder SuppressUnsecuredMessage(bool suppress = true)
    {
        _settings.SuppressUnsecuredMessage = suppress;
        return this;
    }
}

public sealed class DashboardManagementBuilder
{
    private readonly DashboardManagementSettings _settings;

    internal DashboardManagementBuilder(DashboardManagementSettings settings) => _settings = settings;
    public DashboardManagementBuilder Timeout(int seconds)
    {
        _settings.TimeoutSeconds = seconds;
        return this;
    }

    /// <summary>Static fallback source; registration remains the primary path.</summary>
    public DashboardManagementBuilder Source(string name, string url, string? publicUrl = null)
    {
        _settings.AddSource(name, url, publicUrl);
        return this;
    }
}

public sealed class DashboardSecurityBuilder
{
    private readonly DashboardSecuritySettings _settings;

    internal DashboardSecurityBuilder(DashboardSecuritySettings settings) => _settings = settings;
    public DashboardSecurityBuilder AllowAnonymous(bool allow = true)
    {
        _settings.AllowAnonymous = allow;
        return this;
    }
}

internal sealed class DashboardSettings
{
    public DashboardBrandingSettings Branding { get; } = new();
    public DashboardHostingSettings Hosting { get; } = new();
    public DashboardFilesSettings Files { get; } = new();
    public DashboardOtlpSettings Otlp { get; } = new();
    public DashboardManagementSettings Management { get; } = new();
    public DashboardSecuritySettings Security { get; } = new();

    public Dictionary<string, string?> ToConfigurationValues()
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(Branding.Title))
        {
            // The dashboard reads both keys; set both so the host wins over env.
            values["Dashboard:ApplicationName"] = Branding.Title;
            values["ASPIRE_DASHBOARD_APPLICATION_NAME"] = Branding.Title;
        }

        if (!string.IsNullOrWhiteSpace(Hosting.EndpointUrls))
        {
            values["ASPNETCORE_URLS"] = Hosting.EndpointUrls;
        }

        if (!string.IsNullOrWhiteSpace(Hosting.PublicUrl))
        {
            values["Dashboard:Frontend:PublicUrl"] = Hosting.PublicUrl;
        }

        if (!string.IsNullOrWhiteSpace(Files.Title))
        {
            values["Dashboard:Files:Title"] = Files.Title;
        }
        foreach (var root in Files.Roots)
        {
            values[$"Dashboard:Files:Roots:{root.Id}:Path"] = root.Path;
            if (!string.IsNullOrWhiteSpace(root.Title))
            {
                values[$"Dashboard:Files:Roots:{root.Id}:Title"] = root.Title;
            }
            if (!string.IsNullOrWhiteSpace(root.Url))
            {
                values[$"Dashboard:Files:Roots:{root.Id}:Url"] = root.Url;
            }
        }

        if (!string.IsNullOrWhiteSpace(Otlp.GrpcEndpointUrl))
        {
            values["ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL"] = Otlp.GrpcEndpointUrl;
        }
        if (!string.IsNullOrWhiteSpace(Otlp.HttpEndpointUrl))
        {
            values["ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL"] = Otlp.HttpEndpointUrl;
        }
        if (Otlp.Unsecured is true)
        {
            values["Dashboard:Otlp:AuthMode"] = "Unsecured";
        }
        if (Otlp.SuppressUnsecuredMessage is { } suppress)
        {
            values["Dashboard:Otlp:SuppressUnsecuredMessage"] = suppress.ToString();
        }

        if (Security.AllowAnonymous is { } allowAnonymous)
        {
            values["ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS"] = allowAnonymous.ToString();
        }

        if (Management.TimeoutSeconds is { } timeout)
        {
            values["Management:TimeoutSeconds"] = timeout.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        for (var i = 0; i < Management.Sources.Count; i++)
        {
            var source = Management.Sources[i];
            values[$"Management:Sources:{i}:Name"] = source.Name;
            values[$"Management:Sources:{i}:Url"] = source.Url;
            if (source.PublicUrl is not null)
            {
                values[$"Management:Sources:{i}:PublicUrl"] = source.PublicUrl;
            }
        }

        return values;
    }
}

internal sealed class DashboardBrandingSettings
{
    public const string DefaultLogoUrl = "/img/logo-purple.png";

    public string? Title { get; set; }
    public string LogoUrl { get; set; } = DefaultLogoUrl;
}

internal sealed class DashboardHostingSettings
{
    public string? BasePath { get; set; }
    public string? PublicUrl { get; set; }
    public string? EndpointUrls { get; set; }
}

internal sealed class DashboardFilesSettings
{
    public string? Title { get; set; }
    public List<DashboardFilesRootSetting> Roots { get; } = [];

    public DashboardFilesRootSetting? GetRoot(string id) =>
        Roots.FirstOrDefault(root => string.Equals(root.Id, id, StringComparison.OrdinalIgnoreCase));

    public void SetRoot(string id, string? path, string? title, string? url)
    {
        var index = Roots.FindIndex(root => string.Equals(root.Id, id, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(path))
        {
            if (index >= 0)
            {
                Roots.RemoveAt(index);
            }

            return;
        }

        var setting = new DashboardFilesRootSetting(
            id,
            path,
            title,
            string.IsNullOrWhiteSpace(url) ? $"/files/{id}" : url);
        if (index >= 0)
        {
            Roots[index] = setting;
        }
        else
        {
            Roots.Add(setting);
        }
    }
}

internal sealed record DashboardFilesRootSetting(string Id, string Path, string? Title, string Url);

internal sealed class DashboardOtlpSettings
{
    public string? GrpcEndpointUrl { get; set; }
    public string? HttpEndpointUrl { get; set; }
    public bool? Unsecured { get; set; }
    public bool? SuppressUnsecuredMessage { get; set; }
}

internal sealed class DashboardManagementSettings
{
    public int? TimeoutSeconds { get; set; }
    public List<ManagementSourceSetting> Sources { get; } = [];

    public void AddSource(string name, string url, string? publicUrl)
    {
        Sources.Add(new ManagementSourceSetting(name, url, publicUrl));
    }
}

internal sealed record ManagementSourceSetting(string Name, string Url, string? PublicUrl);

internal sealed class DashboardSecuritySettings
{
    public bool? AllowAnonymous { get; set; }
}
