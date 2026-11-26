using System.Diagnostics;
using System.Globalization;
using Aspire.Dashboard.Components.Content;
using Aspire.Dashboard.Management;
using Aspire.Dashboard.Model;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.JSInterop;

namespace Aspire.Dashboard.Components.Pages;

/// <summary>
/// «Поиск» tab: one filter toolbar (a search panel) and a list of action
/// results. Each result is a card with the request parameters in its toolbar
/// and the response rendered by the shared content viewers. The selected filter
/// and every input value are mirrored in the URL, so a link reopens the form
/// with the same query.
/// </summary>
public partial class Search : ComponentBase, IDisposable
{
    private readonly List<FilterState> _filters = [];
    private string _selectedKey = string.Empty;
    private string? _loadError;
    private bool _loading;

    [Inject]
    public required ManagementPanelService ManagementPanels { get; init; }

    [Inject]
    public required NavigationManager NavigationManager { get; init; }

    [Inject]
    public required IJSRuntime JS { get; init; }

    private FilterState? Current =>
        _filters.FirstOrDefault(filter => string.Equals(filter.Key, _selectedKey, StringComparison.Ordinal))
        ?? _filters.FirstOrDefault();

    protected override void OnInitialized()
        => NavigationManager.LocationChanged += OnLocationChanged;

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
        ApplyUrl(new Uri(NavigationManager.Uri));
    }

    public void Dispose()
        => NavigationManager.LocationChanged -= OnLocationChanged;

    private void OnLocationChanged(object? sender, LocationChangedEventArgs args)
    {
        if (_loading)
        {
            return;
        }

        ApplyUrl(new Uri(args.Location));
        _ = InvokeAsync(StateHasChanged);
    }

    private async Task LoadAsync()
    {
        _loading = true;
        _loadError = null;

        try
        {
            var data = await ManagementPanels.GetPanelsAsync();

            foreach (var source in data.Sources)
            {
                foreach (var panel in source.Panels)
                {
                    if (!string.Equals(panel.Kind, "search", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    _filters.Add(FilterState.Create(source, panel));
                }
            }

            if (_filters.Count > 0 &&
                !_filters.Any(filter => string.Equals(filter.Key, _selectedKey, StringComparison.Ordinal)))
            {
                _selectedKey = _filters[0].Key;
            }
        }
        catch (Exception ex)
        {
            _loadError = ex.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnFilterChanged(ChangeEventArgs args)
    {
        _selectedKey = args.Value?.ToString() ?? string.Empty;
        UpdateUrl(replace: false);
    }

    /// <summary>
    /// Restores the selected filter and input values from the query string:
    /// <c>/search?filter=app2%2FCallSearchPanel&amp;Phone=%2B7+999...</c>.
    /// </summary>
    private void ApplyUrl(Uri location)
    {
        var query = QueryHelpers.ParseQuery(location.Query);

        if (query.TryGetValue("filter", out var filter) &&
            !string.IsNullOrWhiteSpace(filter) &&
            _filters.Any(state => string.Equals(state.Key, filter.ToString(), StringComparison.Ordinal)))
        {
            _selectedKey = filter.ToString();
        }

        if (Current is not { } state)
        {
            return;
        }

        foreach (var field in state.Panel.Fields)
        {
            if (field.InputType == "readonly")
            {
                continue;
            }

            var hasValue = query.TryGetValue(field.Name, out var values);
            var value = hasValue ? values.ToString() : null;

            if (field.InputType == "bool")
            {
                state.Bools[field.Name] = hasValue
                    ? string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(value, "on", StringComparison.OrdinalIgnoreCase)
                    : state.InitialBools.GetValueOrDefault(field.Name);
            }
            else
            {
                // A field missing from the query means the form was opened
                // before it was filled: restore the descriptor default.
                state.Text[field.Name] = hasValue
                    ? value ?? string.Empty
                    : state.InitialText.GetValueOrDefault(field.Name, string.Empty);
            }
        }
    }

    /// <summary>
    /// Writes the selected filter and every non-readonly input into the URL.
    /// Called on filter change and when a search is run — never while typing.
    /// </summary>
    private void UpdateUrl(bool replace)
    {
        if (Current is not { } state)
        {
            return;
        }

        var parameters = new Dictionary<string, object?>
        {
            ["filter"] = state.Key
        };

        foreach (var field in state.Panel.Fields)
        {
            if (field.InputType == "readonly")
            {
                continue;
            }

            parameters[field.Name] = field.InputType == "bool"
                ? state.Bools.GetValueOrDefault(field.Name) ? "true" : "false"
                : state.Text.GetValueOrDefault(field.Name);
        }

        var target = new Uri(new Uri(NavigationManager.BaseUri), "search").AbsoluteUri;
        var url = NavigationManager.GetUriWithQueryParameters(target, parameters);

        if (string.Equals(NavigationManager.Uri, url, StringComparison.Ordinal))
        {
            return;
        }

        NavigationManager.NavigateTo(url, replace: replace);
    }

    private async Task OnFieldKeyDown(KeyboardEventArgs args, FilterState state)
    {
        if (string.Equals(args.Key, "Enter", StringComparison.Ordinal) && state.FindAction is { } find)
        {
            await RunActionAsync(state, find);
        }
    }

    private async Task RunActionAsync(FilterState state, ActionDto action)
    {
        if (state.Busy)
        {
            return;
        }

        if (action.RequireConfirm)
        {
            var confirmed = await JS.InvokeAsync<bool>("confirm", $"{action.Label}?");
            if (!confirmed)
            {
                return;
            }
        }

        // Typing never navigates. Each search pushes a history entry with the
        // current filter and values, so Back restores the previous query.
        UpdateUrl(replace: false);

        state.Busy = true;
        state.RunningAction = action.Name;

        var startedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        var parameters = SnapshotParameters(state);
        var tooltip = string.Join(Environment.NewLine, parameters.Select(p => $"{p.Label}: {p.Value}"));

        try
        {
            var values = BuildValues(state);
            var outcome = await ManagementPanels.ExecuteActionAsync(
                state.Source,
                state.Panel.Id,
                action.Name,
                values);

            stopwatch.Stop();

            if (outcome.IsSuccessStatusCode && outcome.Result is not null)
            {
                state.Results.Insert(0, new SearchResultItem(
                    action.Label,
                    state.Source,
                    startedAt,
                    stopwatch.Elapsed,
                    parameters,
                    tooltip,
                    outcome.Result.ToDashboardContent(),
                    null));
            }
            else
            {
                state.Results.Insert(0, new SearchResultItem(
                    action.Label,
                    state.Source,
                    startedAt,
                    stopwatch.Elapsed,
                    parameters,
                    tooltip,
                    null,
                    $"HTTP {outcome.StatusCode}: {outcome.Body}"));
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            state.Results.Insert(0, new SearchResultItem(
                action.Label,
                state.Source,
                startedAt,
                stopwatch.Elapsed,
                parameters,
                tooltip,
                null,
                ex.Message));
        }
        finally
        {
            state.Busy = false;
            state.RunningAction = null;
        }
    }

    private void ResetFilter(FilterState state)
    {
        state.Text.Clear();
        foreach (var pair in state.InitialText)
        {
            state.Text[pair.Key] = pair.Value;
        }

        state.Bools.Clear();
        foreach (var pair in state.InitialBools)
        {
            state.Bools[pair.Key] = pair.Value;
        }

        UpdateUrl(replace: false);
    }

    private static void ClearResults(FilterState state) => state.Results.Clear();

    private static void RemoveResult(FilterState state, SearchResultItem result)
        => state.Results.Remove(result);

    private static void SetText(FilterState state, FieldDto field, ChangeEventArgs args)
        => state.Text[field.Name] = args.Value?.ToString() ?? string.Empty;

    private static void SetBool(FilterState state, FieldDto field, ChangeEventArgs args)
        => state.Bools[field.Name] = args.Value is bool value && value;

    private static string GetInputType(FieldDto field) => field.InputType switch
    {
        "date" => "date",
        "int" or "number" => "number",
        _ => "text"
    };

    private static string GetStep(FieldDto field) => field.InputType switch
    {
        "int" => "1",
        "number" => "any",
        _ => "any"
    };

    private static List<RequestParameter> SnapshotParameters(FilterState state)
    {
        var parameters = new List<RequestParameter>(state.Panel.Fields.Count);

        foreach (var field in state.Panel.Fields)
        {
            var value = field.InputType == "bool"
                ? state.Bools.GetValueOrDefault(field.Name) ? "Да" : "Нет"
                : state.Text.GetValueOrDefault(field.Name);

            parameters.Add(new RequestParameter(
                field.Label,
                string.IsNullOrWhiteSpace(value) ? "—" : value));
        }

        return parameters;
    }

    private static Dictionary<string, object?> BuildValues(FilterState state)
    {
        var values = new Dictionary<string, object?>();

        foreach (var field in state.Panel.Fields)
        {
            if (field.InputType == "readonly")
            {
                continue;
            }

            if (field.InputType == "bool")
            {
                values[field.Name] = state.Bools.GetValueOrDefault(field.Name);
                continue;
            }

            var text = state.Text.GetValueOrDefault(field.Name);

            values[field.Name] = field.InputType switch
            {
                "int" => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)
                    ? integer
                    : null,
                "number" => decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
                    ? number
                    : null,
                _ => text
            };
        }

        return values;
    }

    private sealed record RequestParameter(string Label, string Value);

    private sealed class SearchResultItem(
        string actionLabel,
        string source,
        DateTimeOffset startedAt,
        TimeSpan duration,
        IReadOnlyList<RequestParameter> parameters,
        string requestTooltip,
        DashboardContent? content,
        string? error)
    {
        public string ActionLabel { get; } = actionLabel;
        public string Source { get; } = source;
        public DateTimeOffset StartedAt { get; } = startedAt;
        public TimeSpan Duration { get; } = duration;
        public IReadOnlyList<RequestParameter> Parameters { get; } = parameters;
        public string RequestTooltip { get; } = requestTooltip;
        public DashboardContent? Content { get; } = content;
        public string? Error { get; } = error;

        public string DurationText => Duration.TotalMilliseconds < 1000
            ? $"{Duration.TotalMilliseconds:0} мс"
            : $"{Duration.TotalSeconds:0.00} с";
    }

    private sealed class FilterState
    {
        private FilterState(string source, PanelDto panel)
        {
            Source = source;
            Panel = panel;
            Key = $"{source}/{panel.Id}";
        }

        public string Key { get; }
        public string Source { get; }
        public PanelDto Panel { get; }
        public string DisplayName => $"{Source} · {Panel.Title}";
        public Dictionary<string, string> Text { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, bool> Bools { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> InitialText { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, bool> InitialBools { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<SearchResultItem> Results { get; } = [];
        public bool Busy { get; set; }
        public string? RunningAction { get; set; }

        public ActionDto? FindAction =>
            Panel.Actions.FirstOrDefault(IsFindAction) ?? Panel.Actions.FirstOrDefault();

        public IEnumerable<ActionDto> ExtraActions =>
            Panel.Actions.Where(action => !ReferenceEquals(action, FindAction));

        public static FilterState Create(PanelSourceDto source, PanelDto panel)
        {
            var state = new FilterState(source.Name, panel);

            foreach (var field in panel.Fields)
            {
                if (field.InputType == "bool")
                {
                    state.Bools[field.Name] = field.Value.ValueKind == System.Text.Json.JsonValueKind.True;
                }
                else
                {
                    state.Text[field.Name] = field.Value.ValueKind switch
                    {
                        System.Text.Json.JsonValueKind.String => field.Value.GetString() ?? string.Empty,
                        System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined => string.Empty,
                        _ => field.Value.ToString()
                    };
                }
            }

            foreach (var pair in state.Text)
            {
                state.InitialText[pair.Key] = pair.Value;
            }

            foreach (var pair in state.Bools)
            {
                state.InitialBools[pair.Key] = pair.Value;
            }

            return state;
        }

        private static bool IsFindAction(ActionDto action) =>
            action.Name.Contains("find", StringComparison.OrdinalIgnoreCase) ||
            action.Name.Contains("search", StringComparison.OrdinalIgnoreCase) ||
            action.Label.Contains("найти", StringComparison.OrdinalIgnoreCase) ||
            action.Label.Contains("поиск", StringComparison.OrdinalIgnoreCase);
    }
}
