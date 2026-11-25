namespace Dashboard.Client;

/// <summary>
/// Contract implemented by every dashboard panel class. The registry resolves the
/// panel singleton from DI and calls these members directly — at request time no
/// reflection is involved. Attribute metadata (<c>[DashboardPanel]</c>,
/// <c>[DashboardInput]</c>, <c>[DashboardAction]</c>) is read once during
/// registration, and input accessors are compiled into delegates.
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
