using System.Linq.Expressions;
using System.Reflection;

namespace RaMonitor.Panels;

/// <summary>
/// Fluent registration of dashboard panels. This is the primary API:
/// <code>
/// builder.AddDashboardPanels(panels => panels
///     .Panel&lt;CounterPanel&gt;("Счётчик app1", order: 10)
///         .Input(p => p.Step, "Шаг")
///         .Input(p => p.Enabled, "Активно")
///         .Action(p => p.Apply(), "Применить")
///         .Action(p => p.Reset(), "Сбросить", requireConfirm: true));
/// </code>
/// Attributes are optional sugar: <see cref="ScanAssembly"/> registers every
/// class marked with <see cref="DashboardPanelAttribute"/>.
/// </summary>
public sealed class DashboardPanelBuilder
{
    internal List<DashboardPanelRegistration> Registrations { get; } = [];

    /// <summary>Declares a panel explicitly.</summary>
    public DashboardPanelOf<T> Panel<T>(
        string title,
        int order = 0,
        string? id = null,
        string? description = null)
        where T : class, IDashboardPanel
    {
        var registration = new DashboardPanelRegistration(typeof(T), title, order, id, description);
        Registrations.Add(registration);
        return new DashboardPanelOf<T>(this, registration);
    }

    /// <summary>
    /// Attribute sugar: registers all [DashboardPanel] classes from the given
    /// assemblies (the calling assembly when none is passed).
    /// </summary>
    public DashboardPanelBuilder ScanAssembly(params Assembly[] assemblies)
    {
        var scanned = (assemblies.Length == 0 ? [Assembly.GetCallingAssembly()] : assemblies.Distinct());

        foreach (var assembly in scanned)
        {
            var types = assembly.GetTypes()
                .Where(type => type is { IsClass: true, IsAbstract: false } &&
                               typeof(IDashboardPanel).IsAssignableFrom(type) &&
                               type.GetCustomAttribute<DashboardPanelAttribute>() is not null)
                .OrderBy(type => type.FullName, StringComparer.Ordinal);

            foreach (var type in types)
            {
                Registrations.Add(DashboardPanelRegistration.FromAttributes(type));
            }
        }

        return this;
    }

    /// <summary>Attribute sugar for a single assembly marker type.</summary>
    public DashboardPanelBuilder ScanAssembly<TMarker>() => ScanAssembly(typeof(TMarker).Assembly);
}

/// <summary>Fluent declarations for one panel.</summary>
public sealed class DashboardPanelOf<T> where T : class, IDashboardPanel
{
    private readonly DashboardPanelBuilder _owner;
    private readonly DashboardPanelRegistration _registration;

    internal DashboardPanelOf(DashboardPanelBuilder owner, DashboardPanelRegistration registration)
    {
        _owner = owner;
        _registration = registration;
    }

    /// <summary>Declares a form input bound to a public property or field.</summary>
    public DashboardPanelOf<T> Input<TValue>(
        Expression<Func<T, TValue>> member,
        string label,
        DashboardInputType type = DashboardInputType.Auto,
        int sortOrder = 0,
        string? placeholder = null,
        bool required = false)
    {
        var memberInfo = ResolveMember(member);
        _registration.AddField(new DashboardPanelRegistration.FieldBinding(
            memberInfo,
            label,
            type,
            sortOrder,
            placeholder,
            required));

        return this;
    }

    /// <summary>
    /// Declares a button. Only the method name is used: the panel dispatches it
    /// in <see cref="IDashboardPanel.ExecuteAsync"/>, so the expression gives
    /// compile-time safety instead of a string.
    /// </summary>
    public DashboardPanelOf<T> Action(
        Expression<Func<T, object?>> action,
        string label,
        int sortOrder = 0,
        bool requireConfirm = false,
        string? description = null)
    {
        var method = ResolveMethod(action);
        _registration.AddAction(new DashboardPanelRegistration.ActionBinding(
            method.Name,
            label,
            sortOrder,
            requireConfirm,
            description));

        return this;
    }

    /// <summary>Starts the next panel declaration.</summary>
    public DashboardPanelOf<TNext> Panel<TNext>(
        string title,
        int order = 0,
        string? id = null,
        string? description = null)
        where TNext : class, IDashboardPanel
        => _owner.Panel<TNext>(title, order, id, description);

    private static MemberInfo ResolveMember<TValue>(Expression<Func<T, TValue>> expression)
    {
        var body = expression.Body;
        while (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
        {
            body = unary.Operand;
        }

        return body is MemberExpression { Member: PropertyInfo or FieldInfo } member
            ? member.Member
            : throw new InvalidOperationException(
                "Input must be a property or field access, e.g. p => p.Step.");
    }

    private static MethodInfo ResolveMethod(Expression<Func<T, object?>> expression)
    {
        var body = expression.Body;
        while (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
        {
            body = unary.Operand;
        }

        return body is MethodCallExpression call
            ? call.Method
            : throw new InvalidOperationException(
                "Action must be a method call, e.g. p => p.Apply().");
    }
}
