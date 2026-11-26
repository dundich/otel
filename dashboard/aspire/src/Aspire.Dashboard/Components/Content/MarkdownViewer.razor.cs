using Aspire.Dashboard.Model;
using Aspire.Dashboard.Model.Markdown;
using Aspire.Dashboard.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace Aspire.Dashboard.Components.Content;

public partial class MarkdownViewer : ComponentBase
{
    private MarkdownProcessor? _processor;

    [Parameter, EditorRequired]
    public required DashboardContent Content { get; set; }

    [Inject]
    public required IStringLocalizer<ControlsStrings> ControlsLoc { get; init; }

    protected override void OnParametersSet()
    {
        _processor ??= new MarkdownProcessor(
            ControlsLoc,
            safeUrlSchemes: MarkdownHelpers.SafeUrlSchemes,
            extensions: []);
    }
}
