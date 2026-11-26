using Microsoft.Extensions.Configuration;

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
///         .Logs("/workspace/logs")
///         .Share("/host-home"))
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
        files.LogsPath = configuration["Dashboard:Files:Logs:Path"] ?? files.LogsPath;
        files.LogsTitle = configuration["Dashboard:Files:Logs:Title"] ?? files.LogsTitle;
        files.SharePath = configuration["Dashboard:Files:Share:Path"] ?? files.SharePath;
        files.ShareTitle = configuration["Dashboard:Files:Share:Title"] ?? files.ShareTitle;

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

    /// <summary>File browser tabs. A null path hides the tab.</summary>
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
    public DashboardFilesBuilder Logs(string? path, string? title = null)
    {
        _settings.LogsPath = path;
        _settings.LogsTitle = title;
        return this;
    }

    public DashboardFilesBuilder Share(string? path, string? title = null)
    {
        _settings.SharePath = path;
        _settings.ShareTitle = title;
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

        if (Files.LogsPath is not null)
        {
            values["Dashboard:Files:Logs:Path"] = Files.LogsPath;
        }
        if (Files.LogsTitle is not null)
        {
            values["Dashboard:Files:Logs:Title"] = Files.LogsTitle;
        }
        if (Files.SharePath is not null)
        {
            values["Dashboard:Files:Share:Path"] = Files.SharePath;
        }
        if (Files.ShareTitle is not null)
        {
            values["Dashboard:Files:Share:Title"] = Files.ShareTitle;
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
    public string? LogsPath { get; set; }
    public string? LogsTitle { get; set; }
    public string? SharePath { get; set; }
    public string? ShareTitle { get; set; }
}

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
