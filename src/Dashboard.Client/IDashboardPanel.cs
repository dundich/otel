namespace Dashboard.Client;

/// <summary>
/// Contract implemented by every dashboard panel class. Metadata (title, inputs,
/// actions) is declared in the registration builder
/// (<c>builder.AddDashboardPanels(...)</c>); attributes are optional sugar for
/// <c>ScanAssembly</c>. The registry resolves the panel singleton from DI and
/// calls these members directly — at request time no reflection is involved.
/// </summary>
public interface IDashboardPanel
{
    /// <summary>
    /// Recomputes field values before the panel descriptor is returned to the
    /// dashboard. Invoked on every GET /dashboard/panels. Default: no-op.
    /// </summary>
    Task RenderAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>
    /// Executes the action registered under <paramref name="actionName"/> — the
    /// name of the method marked with <c>[DashboardAction]</c>. Submitted form
    /// values are already bound to the <c>[DashboardInput]</c> members before
    /// this call. The returned object becomes the action message: a string is
    /// shown as-is, null yields "готово", anything else is serialized to JSON.
    /// Default: throws.
    /// </summary>
    Task<object?> ExecuteAsync(string actionName, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException(
            $"Action '{actionName}' is not handled by this panel. " +
            $"Implement {nameof(IDashboardPanel)}.{nameof(ExecuteAsync)}.");
}
