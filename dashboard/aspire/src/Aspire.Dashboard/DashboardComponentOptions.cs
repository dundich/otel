namespace Aspire.Dashboard;

/// <summary>
/// Runtime branding and mount settings resolved by the host. Builders populate
/// these values; the dashboard itself owns no configuration.
/// </summary>
public sealed class DashboardComponentOptions
{
    private string _basePath = "/";

    /// <summary>
    /// Mount path of the dashboard inside the host, e.g. "/" or "/monitor".
    /// </summary>
    public string BasePath
    {
        get => _basePath;
        set => _basePath = NormalizeBasePath(value);
    }

    /// <summary>
    /// Public URL shown in the UI and used for links. Hosts can override it
    /// because the same dashboard can be mounted under different base URLs.
    /// </summary>
    public string? PublicUrl { get; set; }

    /// <summary>Header and document title. Defaults to the dashboard application name.</summary>
    public string? Title { get; set; }

    /// <summary>Logo URL or host-relative path.</summary>
    public string LogoUrl { get; set; } = "/img/logo-purple.png";

    /// <summary>Base href for Blazor, always with a trailing slash.</summary>
    public string BaseHref => _basePath == "/" ? "/" : _basePath + "/";

    /// <summary>Logo URL with the mount path applied for host-relative paths.</summary>
    public string ResolvedLogoUrl =>
        LogoUrl.StartsWith('/') && !LogoUrl.StartsWith("//") && _basePath != "/"
            ? _basePath + LogoUrl
            : LogoUrl;

    private static string NormalizeBasePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "/")
        {
            return "/";
        }

        var path = value.Trim();
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        return path.TrimEnd('/');
    }
}
