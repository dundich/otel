using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Dashboard.Model;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MessageBarIntent = Microsoft.FluentUI.AspNetCore.Components.MessageBarIntent;

namespace Aspire.Dashboard.Components.Pages;

public partial class Management : ComponentBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private List<PanelState> _states = [];
    private PanelsResponse? _data;
    private string _selectedSource = string.Empty;
    private string? _loadError;
    private bool _loading;

    [Inject]
    public required IConfiguration Configuration { get; init; }

    [Inject]
    public required IJSRuntime JS { get; init; }

    [Inject]
    public required INotificationService NotificationService { get; init; }

    [Inject]
    public required NavigationManager NavigationManager { get; init; }

    [Parameter]
    public string? ResourceName { get; set; }

    private string ApiBase =>
        (Configuration["Management:ApiBaseUrl"] ?? "http://app2:8080/api").TrimEnd('/');

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
            _data = await _http.GetFromJsonAsync<PanelsResponse>($"{ApiBase}/panels", JsonOptions)
                    ?? new PanelsResponse();

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
                    source.PublicUrl,
                    source.Url,
                    source.DnsName,
                    source.Addresses);

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
            var url = $"{ApiBase}/panels/{Uri.EscapeDataString(state.Source)}/" +
                      $"{Uri.EscapeDataString(state.Panel.Id)}/actions/{Uri.EscapeDataString(action.Name)}";

            using var response = await _http.PostAsJsonAsync(url, new { values }, JsonOptions);
            var body = await response.Content.ReadAsStringAsync();

            ActionResultDto? result = null;
            try
            {
                result = JsonSerializer.Deserialize<ActionResultDto>(body, JsonOptions);
            }
            catch (JsonException)
            {
                // Keep the raw body for the status below.
            }

            if (!response.IsSuccessStatusCode || result is null)
            {
                state.Success = false;
                state.Status = $"HTTP {(int)response.StatusCode}: {body}";
            }
            else
            {
                state.Success = result.Success;
                state.Status = result.Message ?? (result.Success ? "Готово" : "Ошибка");

                if (result.Panel is not null)
                {
                    // The action response carries the full panel snapshot. Replace all
                    // field values, not only the ones that were edited.
                    state.Panel = result.Panel;
                    ApplyPanelValues(state, result.Panel);
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
            string publicUrl,
            string url,
            string dnsName,
            IReadOnlyList<string> addresses)
        {
            Source = source;
            Panel = panel;
            PublicUrl = publicUrl;
            Url = url;
            DnsName = dnsName;
            Addresses = addresses;
        }

        public string Source { get; }
        public PanelDto Panel { get; set; }
        public string PublicUrl { get; }
        public string Url { get; }
        public string DnsName { get; }
        public IReadOnlyList<string> Addresses { get; }
        public Dictionary<string, string> Text { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, bool> Bools { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool Busy { get; set; }
        public bool Success { get; set; }
        public string? Status { get; set; }
    }

    private sealed class PanelsResponse
    {
        public List<PanelSourceDto> Sources { get; set; } = [];
        public List<string> Errors { get; set; } = [];
    }

    private sealed class PanelSourceDto
    {
        public string Name { get; set; } = string.Empty;
        public string PublicUrl { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string DnsName { get; set; } = string.Empty;
        public List<string> Addresses { get; set; } = [];
        public List<PanelDto> Panels { get; set; } = [];
    }

    private sealed class PanelDto
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Version { get; set; } = "0.0.0";
        public string? Description { get; set; }
        public List<FieldDto> Fields { get; set; } = [];
        public List<ActionDto> Actions { get; set; } = [];
    }

    private sealed class FieldDto
    {
        public string Name { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string InputType { get; set; } = "text";
        public string? Placeholder { get; set; }
        public bool Required { get; set; }
        public JsonElement Value { get; set; }
    }

    private sealed class ActionDto
    {
        public string Name { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public bool RequireConfirm { get; set; }
        public string? Description { get; set; }
    }

    private sealed class ActionResultDto
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public PanelDto? Panel { get; set; }
    }
}
