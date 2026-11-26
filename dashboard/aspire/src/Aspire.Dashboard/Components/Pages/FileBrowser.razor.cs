using System.Globalization;
using System.IO.Enumeration;
using System.Text;
using Aspire.Dashboard.Components.Files;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Microsoft.FluentUI.AspNetCore.Components;
using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons;

namespace Aspire.Dashboard.Components.Pages;

public partial class FileBrowser : ComponentBase, IAsyncDisposable
{
    private static readonly HashSet<string> s_textExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".json", ".xml", ".md", ".csv", ".yml", ".yaml",
        ".cs", ".js", ".ts", ".css", ".html", ".razor", ".props", ".targets",
        ".slnx", ".sh", ".ps1", ".env", ".config", ".ini"
    };

    private FileBrowserRoot? _root;
    private FileBrowserRoot? _selectedRoot;
    private string _relativePath = string.Empty;
    private string _mask = "*";
    private string _appliedMask = "*";
    private List<FileEntry> _entries = [];
    private PreviewState? _preview;
    private string? _error;
    private ElementReference _previewElement;
    private PreviewState? _highlightedPreview;
    private IJSObjectReference? _jsModule;

    [Parameter]
    public string? Slug { get; set; }

    [Parameter]
    public string? RelativePath { get; set; }

    [Inject]
    public required FileBrowserRootService Tabs { get; init; }

    [Inject]
    public required NavigationManager NavigationManager { get; init; }

    [Inject]
    public required IJSRuntime JS { get; init; }

    private string Title => Tabs.Title;

    private IReadOnlyList<FileBrowserRoot> Roots => Tabs.Roots;

    private bool IsRoot => string.IsNullOrEmpty(_relativePath);

    private string CurrentPath => IsRoot ? "/" : $"/{_relativePath}";

    protected override async Task OnParametersSetAsync()
    {
        await ReloadAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_preview is null || _preview == _highlightedPreview || _preview.Language is null)
        {
            return;
        }

        _jsModule ??= await JS.InvokeAsync<IJSObjectReference>(
            "import", $"/{Assets["Components/Pages/FileBrowser.razor.js"]}");

        await _jsModule.InvokeVoidAsync("highlight", _previewElement, _preview.Language);
        _highlightedPreview = _preview;
    }

    private async Task ReloadAsync()
    {
        _preview = null;
        _highlightedPreview = null;
        _error = null;

        if (string.IsNullOrEmpty(Slug))
        {
            // /files without a root opens the first configured root.
            var first = Tabs.Roots.FirstOrDefault();
            if (first is null)
            {
                _root = null;
                _selectedRoot = null;
                _entries = [];
                _error = "Файлы не настроены. Укажите Dashboard:Files:Roots.";
                return;
            }

            NavigationManager.NavigateTo(first.Url, replace: true);
            return;
        }

        var root = Tabs.GetBySlug(Slug);
        if (root is null)
        {
            _root = null;
            _selectedRoot = null;
            _entries = [];
            _error = $"Корень файлов «{Slug}» не настроен. Проверьте Dashboard:Files:Roots.";
            return;
        }

        _root = root;
        _selectedRoot = root;
        _relativePath = NormalizeRelativePath(RelativePath);
        await LoadEntriesAsync();
    }

    private async Task LoadEntriesAsync()
    {
        _error = null;
        _entries = [];

        if (_root is null)
        {
            return;
        }

        var fullPath = FileBrowserPath.Resolve(_root.RootPath, _relativePath);
        if (fullPath is null)
        {
            _error = "Путь выходит за пределы разрешённого корня.";
            return;
        }

        if (!Directory.Exists(fullPath))
        {
            _error = $"Директория не найдена: {fullPath}";
            return;
        }

        var masks = ParseMasks(_appliedMask);
        var directory = new DirectoryInfo(fullPath);

        try
        {
            var directories = directory.GetDirectories()
                .Where(d => Matches(d.Name, masks))
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .Select(d => CreateEntry(d.Name, GetRelativePath(d.Name), true, 0, d.LastWriteTimeUtc, "Папка"));

            var files = directory.GetFiles()
                .Where(f => Matches(f.Name, masks))
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .Select(f => CreateEntry(f.Name, GetRelativePath(f.Name), false, f.Length, f.LastWriteTimeUtc, GetFileType(f.Name)));

            _entries = directories.Concat(files).ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            _error = ex.Message;
        }

        await Task.CompletedTask;
    }

    private string GetRelativePath(string name) =>
        string.IsNullOrEmpty(_relativePath) ? name : $"{_relativePath}/{name}";

    private static FileEntry CreateEntry(string name, string relativePath, bool isDirectory, long size, DateTime lastWriteTimeUtc, string type) =>
        new(name, relativePath, isDirectory, size, new DateTimeOffset(lastWriteTimeUtc, TimeSpan.Zero), type);

    private static string GetFileType(string name)
    {
        var extension = System.IO.Path.GetExtension(name).TrimStart('.');
        return string.IsNullOrEmpty(extension) ? "File" : extension.ToUpperInvariant();
    }

    private static string NormalizeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalized = path.Replace('\\', '/').Trim('/');
        return normalized;
    }

    private static string[] ParseMasks(string value)
    {
        var masks = value.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return masks.Length == 0 ? ["*"] : masks;
    }

    private static bool Matches(string name, string[] masks) =>
        masks.Any(mask => FileSystemName.MatchesSimpleExpression(mask, name, ignoreCase: true));

    private string DownloadUrl(FileEntry entry) =>
        $"/files/download?root={Uri.EscapeDataString(_root!.Id)}&path={Uri.EscapeDataString(entry.RelativePath)}";

    private void Navigate(FileBrowserRoot root, string relativePath)
    {
        _preview = null;

        var url = root.Url;
        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            var segments = relativePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString);
            url = $"{root.Url}/{string.Join('/', segments)}";
        }

        NavigationManager.NavigateTo(url);
    }

    private void OnRootChanged()
    {
        if (_selectedRoot is { } root)
        {
            Navigate(root, string.Empty);
        }
    }

    private void OpenEntry(FileEntry entry)
    {
        if (entry.IsDirectory && _root is { } root)
        {
            Navigate(root, entry.RelativePath);
        }
    }

    private void GoRoot()
    {
        if (_root is { } root)
        {
            Navigate(root, string.Empty);
        }
    }

    private void GoUp()
    {
        if (IsRoot || _root is not { } root)
        {
            return;
        }

        var index = _relativePath.LastIndexOf('/');
        Navigate(root, index < 0 ? string.Empty : _relativePath[..index]);
    }

    private void ApplyMask()
    {
        _appliedMask = string.IsNullOrWhiteSpace(_mask) ? "*" : _mask.Trim();
        _ = LoadEntriesAsync();
    }

    private void ClearMask()
    {
        _mask = "*";
        _appliedMask = "*";
        _ = LoadEntriesAsync();
    }

    private void OnMaskKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Enter")
        {
            ApplyMask();
        }
    }

    private void OnKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Home")
        {
            GoRoot();
        }
    }

    private async Task PreviewAsync(FileEntry entry)
    {
        _preview = null;
        if (_root is null)
        {
            return;
        }

        var fullPath = FileBrowserPath.Resolve(_root.RootPath, entry.RelativePath);
        if (fullPath is null || !File.Exists(fullPath))
        {
            _error = "Файл не найден.";
            return;
        }

        if (!IsPreviewable(entry))
        {
            _error = "Просмотр доступен только для текстовых файлов до 5 МБ.";
            return;
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(fullPath);
            if (bytes.AsSpan().IndexOf((byte)0) >= 0)
            {
                _error = "Файл содержит двоичные данные.";
                return;
            }

            var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
            _preview = new PreviewState(entry, text, GetLanguage(entry));
            _highlightedPreview = null;
            _error = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            _error = ex is DecoderFallbackException
                ? "Файл не является корректным UTF-8."
                : ex.Message;
        }
    }

    private void ClosePreview()
    {
        _preview = null;
        _highlightedPreview = null;
    }

    private static string? GetLanguage(FileEntry entry) =>
        System.IO.Path.GetExtension(entry.Name).ToLowerInvariant() switch
        {
            ".json" => "json",
            ".yaml" or ".yml" => "yaml",
            _ => null
        };

    private static bool IsPreviewable(FileEntry entry) =>
        !entry.IsDirectory &&
        entry.Size <= 5 * 1024 * 1024 &&
        s_textExtensions.Contains(System.IO.Path.GetExtension(entry.Name));

    public async ValueTask DisposeAsync()
    {
        if (_jsModule is not null)
        {
            await _jsModule.DisposeAsync();
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes} B"
            : $"{value.ToString("0.#", CultureInfo.InvariantCulture)} {units[unit]}";
    }

    private static Icon GetIcon(FileEntry entry)
    {
        if (entry.IsDirectory)
        {
            return new Icons.Regular.Size16.FolderOpen();
        }

        return System.IO.Path.GetExtension(entry.Name).ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".svg" or ".webp" => new Icons.Regular.Size16.Image(),
            ".log" or ".txt" or ".md" or ".json" or ".xml" or ".csv" or ".yml" or ".yaml" => new Icons.Regular.Size16.DocumentText(),
            _ => new Icons.Regular.Size16.Document()
        };
    }

    private sealed record FileEntry(
        string Name,
        string RelativePath,
        bool IsDirectory,
        long Size,
        DateTimeOffset Modified,
        string Type);

    private sealed record PreviewState(FileEntry Entry, string Text, string? Language);
}
