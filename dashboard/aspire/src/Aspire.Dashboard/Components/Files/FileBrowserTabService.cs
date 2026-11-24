
namespace Aspire.Dashboard.Components.Files;

public sealed record FileBrowserTab(string Id, string Title, string RootPath);

/// <summary>
/// File browser tabs are optional: a tab is registered only when its root path
/// is configured. Missing path means the tab is not rendered at all.
/// </summary>
public sealed class FileBrowserTabService
{
    public FileBrowserTabService(IConfiguration configuration)
    {
        Logs = Create("logs", configuration["Dashboard:Files:Logs:Title"] ?? "Логи",
            configuration["Dashboard:Files:Logs:Path"]);
        Share = Create("share", configuration["Dashboard:Files:Share:Title"] ?? "Share",
            configuration["Dashboard:Files:Share:Path"]);
    }

    public FileBrowserTab? Logs { get; }
    public FileBrowserTab? Share { get; }

    public FileBrowserTab? GetById(string id) =>
        new[] { Logs, Share }.FirstOrDefault(tab => string.Equals(tab?.Id, id, StringComparison.OrdinalIgnoreCase));

    private static FileBrowserTab? Create(string id, string title, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return new FileBrowserTab(id, title, Path.GetFullPath(path));
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
