using Aspire.Dashboard.Model;
using Microsoft.AspNetCore.Components;

namespace Aspire.Dashboard.Components.Content;

public partial class JsonViewer : ComponentBase
{
    [Parameter, EditorRequired]
    public required DashboardContent Content { get; set; }
}
