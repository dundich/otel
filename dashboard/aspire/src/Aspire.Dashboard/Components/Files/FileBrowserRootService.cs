namespace Aspire.Dashboard.Components.Files;

/// <summary>
/// A configured file browser root. Every root has its own title, filesystem
/// path and app-relative URL, so it can be bookmarked and linked directly.
/// </summary>
public sealed record FileBrowserRoot(string Id, string Title, string Url, string RootPath)
{
    /// <summary>The route segment under /files, for example "logs" for /files/logs.</summary>
    public string Slug { get; } = GetSlug(Url);

    private static string GetSlug(string url)
    {
        var segments = url.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 0 ? string.Empty : segments[^1];
    }
}

/// <summary>
/// File browser roots are optional: a root is registered only when its path is
/// configured. Missing path means the root is not shown in the selector.
/// </summary>
public sealed class FileBrowserRootService
{
    public const string DefaultTitle = "Файлы";
    public const string BasePath = "files";

    public FileBrowserRootService(IConfiguration configuration)
    {
        var title = configuration["Dashboard:Files:Title"];
        Title = string.IsNullOrWhiteSpace(title) ? DefaultTitle : title;
        Roots = configuration.GetSection("Dashboard:Files:Roots")
            .GetChildren()
            .Select(child => Create(child.Key, child["Title"], child["Path"], child["Url"]))
            .OfType<FileBrowserRoot>()
            .ToList();
    }

    /// <summary>Title of the single "Files" tab in the dashboard navigation.</summary>
    public string Title { get; }

    public IReadOnlyList<FileBrowserRoot> Roots { get; }

    public FileBrowserRoot? GetBySlug(string? slug) =>
        Roots.FirstOrDefault(root => string.Equals(root.Slug, slug, StringComparison.OrdinalIgnoreCase));

    public FileBrowserRoot? GetById(string? id) =>
        Roots.FirstOrDefault(root => string.Equals(root.Id, id, StringComparison.OrdinalIgnoreCase));

    private static FileBrowserRoot? Create(string id, string? title, string? path, string? url)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return new FileBrowserRoot(
            id,
            string.IsNullOrWhiteSpace(title) ? id : title,
            NormalizeUrl(url, id),
            Path.GetFullPath(path));
    }

    private static string NormalizeUrl(string? url, string id)
    {
        var value = string.IsNullOrWhiteSpace(url) ? $"/{BasePath}/{id}" : url.Trim();
        if (!value.StartsWith('/'))
        {
            value = $"/{value}";
        }

        // The Blazor route is /files/{slug}/{**path}; keep every root under it.
        if (!value.StartsWith($"/{BasePath}/", StringComparison.OrdinalIgnoreCase))
        {
            value = $"/{BasePath}/{id}";
        }

        return value.TrimEnd('/');
    }
}

public static class FileBrowserPath
{
    /// <summary>
    /// Resolves a browser-relative path under the configured root and rejects traversal.
    /// </summary>
    public static string? Resolve(string root, string? relativePath)
    {
        var rootFull = Path.GetFullPath(root);
        if (string.IsNullOrEmpty(relativePath))
        {
            return rootFull;
        }

        if (Path.IsPathRooted(relativePath))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(Path.Combine(rootFull, relativePath));
        var relative = Path.GetRelativePath(rootFull, fullPath);
        if (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return null;
        }

        return fullPath;
    }
}
