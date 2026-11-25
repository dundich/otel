using System.Globalization;
using System.Text.Json;
using Aspire.Dashboard.Management;
using Aspire.Dashboard.Model;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MessageBarIntent = Microsoft.FluentUI.AspNetCore.Components.MessageBarIntent;

namespace Aspire.Dashboard.Components.Pages;

public partial class Management : ComponentBase
{
    private List<PanelState> _states = [];
    private PanelsResponse? _data;
    private string _selectedSource = string.Empty;
    private string? _loadError;
    private bool _loading;

    [Inject]
    public required ManagementPanelService ManagementPanels { get; init; }

    [Inject]
    public required IJSRuntime JS { get; init; }

    [Inject]
    public required INotificationService NotificationService { get; init; }

    [Inject]
    public required NavigationManager NavigationManager { get; init; }

    [Parameter]
    public string? ResourceName { get; set; }

    private IEnumerable<string> Sources =>
        _data?.Sources.Select(source => source.Name) ?? [];

    private IEnumerable<PanelState> VisibleStates =>
        string.IsNullOrEmpty(_selectedSource)
            ? _states
            : _states.Where(state => string.Equals(state.Source, _selectedSource, StringComparison.OrdinalIgnoreCase));

    protected override Task OnInitializedAsync() => LoadAsync();

    protected override async Task OnParametersSetAsync()
    {
        _selectedSource = ResourceName ?? string.Empty;

        if (_data is null)
        {
            await LoadAsync();
        }
    }

    private void OnFilterChanged(ChangeEventArgs args)
    {
        _selectedSource = args.Value?.ToString() ?? string.Empty;

        var url = string.IsNullOrEmpty(_selectedSource)
            ? "/management"
            : $"/management/resource/{Uri.EscapeDataString(_selectedSource)}";

        NavigationManager.NavigateTo(url, replace: true);
    }

    private async Task LoadAsync()
    {
        _loading = true;
        _loadError = null;

        try
        {
            _data = await ManagementPanels.GetPanelsAsync();

            _states = BuildStates(_data);

            if (!string.IsNullOrEmpty(_selectedSource) &&
                !_data.Sources.Any(source => string.Equals(source.Name, _selectedSource, StringComparison.OrdinalIgnoreCase)))
            {
                _selectedSource = string.Empty;
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

    private static List<PanelState> BuildStates(PanelsResponse data)
    {
        var states = new List<PanelState>();

        foreach (var source in data.Sources)
        {
            foreach (var panel in source.Panels)
            {
                var state = new PanelState(
                    source.Name,
                    panel,
                    source.PublicUrl);

                ApplyPanelValues(state, panel);
                states.Add(state);
            }
        }

        return states;
    }

    private static void ApplyPanelValues(PanelState state, PanelDto panel)
    {
        state.Text.Clear();
        state.Bools.Clear();

        foreach (var field in panel.Fields)
        {
            if (field.InputType == "bool")
            {
                state.Bools[field.Name] = field.Value.ValueKind is JsonValueKind.True;
            }
            else
            {
                state.Text[field.Name] = field.Value.ValueKind switch
                {
                    JsonValueKind.String => field.Value.GetString() ?? string.Empty,
                    JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
                    _ => field.Value.ToString()
                };
            }
        }
    }

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

    private static void SetText(PanelState state, FieldDto field, ChangeEventArgs args)
        => state.Text[field.Name] = args.Value?.ToString() ?? string.Empty;

    private static void SetBool(PanelState state, FieldDto field, ChangeEventArgs args)
        => state.Bools[field.Name] = args.Value is bool value && value;

    private async Task RunAsync(PanelState state, ActionDto action)
    {
        if (action.RequireConfirm)
        {
            var confirmed = await JS.InvokeAsync<bool>("confirm", $"{action.Label}?");
            if (!confirmed)
            {
                return;
            }
        }

        state.Busy = true;
        state.Status = $"{action.Label}…";
        state.Success = true;

        try
        {
            var values = BuildValues(state);
            var outcome = await ManagementPanels.ExecuteActionAsync(
                state.Source,
                state.Panel.Id,
                action.Name,
                values);

            if (!outcome.IsSuccessStatusCode || outcome.Result is null)
            {
                state.Success = false;
                state.Status = $"HTTP {outcome.StatusCode}: {outcome.Body}";
            }
            else
            {
                state.Success = outcome.Result.Success;
                state.Status = outcome.Result.Message ?? (outcome.Result.Success ? "Готово" : "Ошибка");

                if (outcome.Result.Panel is not null)
                {
                    // The action response carries the full panel snapshot. Replace all
                    // field values, not only the ones that were edited.
                    state.Panel = outcome.Result.Panel;
                    ApplyPanelValues(state, outcome.Result.Panel);
                }
            }
        }
        catch (Exception ex)
        {
            state.Success = false;
            state.Status = $"Ошибка: {ex.Message}";
        }
        finally
        {
            state.Busy = false;
            AddNotification(state, action);
        }
    }

    private static Dictionary<string, object?> BuildValues(PanelState state)
    {
        var values = new Dictionary<string, object?>();

        foreach (var field in state.Panel.Fields)
        {
            if (field.InputType == "readonly")
            {
                // Display-only value: the app decides its own state.
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

    private void AddNotification(PanelState state, ActionDto action)
    {
        NotificationService.AddNotification(new NotificationEntry
        {
            Title = $"{state.Source} · {state.Panel.Title} · {action.Label}",
            Body = state.Status ?? (state.Success ? "Готово" : "Ошибка"),
            Intent = state.Success ? MessageBarIntent.Success : MessageBarIntent.Error,
            Timestamp = DateTimeOffset.Now
        });
    }

    private sealed class PanelState
    {
        public PanelState(
            string source,
            PanelDto panel,
            string publicUrl)
        {
            Source = source;
            Panel = panel;
            PublicUrl = publicUrl;
        }

        public string Source { get; }
        public PanelDto Panel { get; set; }
        public string PublicUrl { get; }
        public Dictionary<string, string> Text { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, bool> Bools { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool Busy { get; set; }
        public bool Success { get; set; }
        public string? Status { get; set; }
    }
}
