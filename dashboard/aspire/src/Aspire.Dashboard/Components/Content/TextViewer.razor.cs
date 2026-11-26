using Aspire.Dashboard.Model;
using Aspire.Dashboard.Utils;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Aspire.Dashboard.Components.Content;

public partial class TextViewer : ComponentBase, IAsyncDisposable
{
    private ElementReference _element;
    private IJSObjectReference? _js;
    private string? _renderedText;
    private string? _renderedLanguage;

    [Parameter, EditorRequired]
    public required DashboardContent Content { get; set; }

    [Inject]
    public required IJSRuntime JS { get; init; }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _js = await JS.InvokeAsync<IJSObjectReference>(
                "import", $"/{Assets["Components/Content/TextViewer.razor.js"]}");
        }

        var language = GetLanguage(Content.Type);
        if (_js is not null &&
            language is not null &&
            (_renderedText != Content.Content || _renderedLanguage != language))
        {
            _renderedText = Content.Content;
            _renderedLanguage = language;
            await _js.InvokeVoidAsync("highlight", _element, language);
        }
    }

    private static string? GetLanguage(string? type) =>
        ContentViewerRegistry.Normalize(type) switch
        {
            DashboardContentTypes.Json => "json",
            "yaml" or "yml" => "yaml",
            _ => null
        };

    public async ValueTask DisposeAsync()
    {
        if (_js is not null)
        {
            try
            {
                await _js.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The circuit is already gone.
            }
        }
    }
}
