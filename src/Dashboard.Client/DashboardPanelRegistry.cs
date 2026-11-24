using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dashboard.Client;

internal sealed class DashboardPanelRegistration
{
    public DashboardPanelRegistration(Type panelType)
    {
        PanelType = panelType;
        Attribute = panelType.GetCustomAttribute<DashboardPanelAttribute>()
            ?? throw new InvalidOperationException($"Type '{panelType.FullName}' has no {nameof(DashboardPanelAttribute)}.");
        Id = string.IsNullOrWhiteSpace(Attribute.Id) ? panelType.Name : Attribute.Id!;

        Fields = panelType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => (Member: (MemberInfo)property, Attribute: property.GetCustomAttribute<DashboardInputAttribute>()))
            .Concat(panelType.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Select(field => (Member: (MemberInfo)field, Attribute: field.GetCustomAttribute<DashboardInputAttribute>())))
            .Where(x => x.Attribute is not null)
            .Select(x => new FieldBinding(x.Member, x.Attribute!))
            .OrderBy(x => x.Attribute.SortOrder)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Actions = panelType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(method => (Method: method, Attribute: method.GetCustomAttribute<DashboardActionAttribute>()))
            .Where(x => x.Attribute is not null)
            .Select(x =>
            {
                ValidateSignature(x.Method, x.Attribute!.Label);
                return new ActionBinding(x.Method, x.Attribute!);
            })
            .OrderBy(x => x.Attribute.SortOrder)
            .ThenBy(x => x.Method.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (Actions.GroupBy(a => a.Method.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
        {
            throw new InvalidOperationException(
                $"Panel '{Id}' has overloaded [DashboardAction] methods. Action method names must be unique.");
        }

        RenderMethod = panelType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(method =>
                (method.Name is "Render" or "RenderAsync") &&
                (method.GetParameters().Length == 0 ||
                 (method.GetParameters().Length == 1 && method.GetParameters()[0].ParameterType == typeof(CancellationToken))));
    }

    public Type PanelType { get; }
    public DashboardPanelAttribute Attribute { get; }
    public string Id { get; }
    public IReadOnlyList<FieldBinding> Fields { get; }
    public IReadOnlyList<ActionBinding> Actions { get; }
    public MethodInfo? RenderMethod { get; }
    public SemaphoreSlim Gate { get; } = new(1, 1);

    private static void ValidateSignature(MethodInfo method, string label)
    {
        var parameters = method.GetParameters();
        if (parameters.Length > 1 ||
            (parameters.Length == 1 && parameters[0].ParameterType != typeof(CancellationToken)))
        {
            throw new InvalidOperationException(
                $"Action '{label}' / '{method.Name}' must be parameterless or accept only CancellationToken. " +
                "Panel inputs are bound to properties before the action is invoked.");
        }
    }

    public sealed class FieldBinding
    {
        public FieldBinding(MemberInfo member, DashboardInputAttribute attribute)
        {
            Member = member;
            Attribute = attribute;
            ValueType = member switch
            {
                PropertyInfo property => property.PropertyType,
                FieldInfo field => field.FieldType,
                _ => throw new InvalidOperationException($"Unsupported member '{member.Name}'.")
            };

            InputType = ResolveInputType(ValueType, attribute.Type).ToString().ToLowerInvariant();

            switch (member)
            {
                case PropertyInfo property when property.GetMethod is not null && property.SetMethod is not null:
                    Get = property.GetValue;
                    Set = (instance, value) => property.SetValue(instance, value);
                    break;
                case FieldInfo field:
                    Get = field.GetValue;
                    Set = (instance, value) => field.SetValue(instance, value);
                    break;
                case PropertyInfo property:
                    throw new InvalidOperationException(
                        $"Input '{property.Name}' must have a public getter and setter.");
                default:
                    throw new InvalidOperationException($"Unsupported member '{member.Name}'.");
            }
        }

        public MemberInfo Member { get; }
        public DashboardInputAttribute Attribute { get; }
        public Type ValueType { get; }
        public string Name => Member.Name;
        public string InputType { get; }
        public Func<object, object?> Get { get; }
        public Action<object, object?> Set { get; }

        public static DashboardInputType ResolveInputType(Type valueType, DashboardInputType configured)
        {
            if (configured != DashboardInputType.Auto)
            {
                return configured;
            }

            var type = Nullable.GetUnderlyingType(valueType) ?? valueType;
            if (type == typeof(bool)) return DashboardInputType.Bool;
            if (type == typeof(DateOnly) || type == typeof(DateTime) || type == typeof(DateTimeOffset)) return DashboardInputType.Date;
            if (type == typeof(int) || type == typeof(long) || type == typeof(short)) return DashboardInputType.Int;
            if (type == typeof(decimal) || type == typeof(double) || type == typeof(float)) return DashboardInputType.Number;
            return DashboardInputType.Text;
        }
    }

    public sealed record ActionBinding(MethodInfo Method, DashboardActionAttribute Attribute);
}

public sealed class DashboardPanelRegistry
{
    private static readonly string AppVersion = GetAppVersion();
    private readonly IServiceProvider _services;
    private readonly ILogger<DashboardPanelRegistry> _logger;
    private readonly IReadOnlyList<DashboardPanelRegistration> _panels;

    public DashboardPanelRegistry(IServiceProvider services, ILogger<DashboardPanelRegistry> logger)
    {
        _services = services;
        _logger = logger;
        _panels = services.GetServices<DashboardPanelRegistration>()
            .OrderBy(x => x.Attribute.Order)
            .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var duplicate = _panels
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Duplicate dashboard panel id '{duplicate.Key}'. Set a unique [DashboardPanel(..., Id = \"...\")].");
        }
    }

    public async Task<IReadOnlyList<DashboardPanelDescriptor>> GetPanelsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<DashboardPanelDescriptor>(_panels.Count);

        foreach (var registration in _panels)
        {
            await registration.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var instance = _services.GetRequiredService(registration.PanelType);
                if (registration.RenderMethod is not null)
                {
                    await InvokeAsync(instance, registration.RenderMethod, cancellationToken).ConfigureAwait(false);
                }

                result.Add(BuildDescriptor(registration, instance));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dashboard panel {PanelId} failed to render", registration.Id);
            }
            finally
            {
                registration.Gate.Release();
            }
        }

        return result;
    }

    public async Task<DashboardActionResult?> ExecuteAsync(
        string panelId,
        string actionName,
        IReadOnlyDictionary<string, JsonElement>? values,
        CancellationToken cancellationToken = default)
    {
        var registration = _panels.FirstOrDefault(x =>
            string.Equals(x.Id, panelId, StringComparison.OrdinalIgnoreCase));

        var action = registration?.Actions.FirstOrDefault(x =>
            string.Equals(x.Method.Name, actionName, StringComparison.OrdinalIgnoreCase));

        if (registration is null || action is null)
        {
            return null;
        }

        await registration.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var instance = _services.GetRequiredService(registration.PanelType);

            try
            {
                BindValues(registration, instance, values);
                var data = await InvokeAsync(instance, action.Method, cancellationToken).ConfigureAwait(false);

                var message = data switch
                {
                    null => $"{action.Attribute.Label}: готово",
                    string text => text,
                    _ => JsonSerializer.Serialize(data)
                };

                return new DashboardActionResult(
                    Success: true,
                    Message: message,
                    Panel: BuildDescriptor(registration, instance),
                    Data: data);
            }
            catch (Exception ex)
            {
                var error = ex is TargetInvocationException { InnerException: not null } invocation
                    ? invocation.InnerException!
                    : ex;

                _logger.LogWarning(error, "Dashboard action {PanelId}/{ActionName} failed", registration.Id, action.Method.Name);

                return new DashboardActionResult(
                    Success: false,
                    Message: error.Message,
                    Panel: BuildDescriptor(registration, instance));
            }
        }
        finally
        {
            registration.Gate.Release();
        }
    }

    private static DashboardPanelDescriptor BuildDescriptor(DashboardPanelRegistration registration, object instance)
    {
        var fields = registration.Fields
            .Select(field => new DashboardFieldDescriptor(
                Name: field.Name,
                Label: field.Attribute.Label,
                InputType: field.InputType,
                Order: field.Attribute.SortOrder,
                Placeholder: field.Attribute.Placeholder,
                Required: field.Attribute.Required,
                Value: FormatValue(field.Get(instance), field.ValueType)))
            .ToArray();

        var actions = registration.Actions
            .Select(action => new DashboardActionDescriptor(
                Name: action.Method.Name,
                Label: action.Attribute.Label,
                Order: action.Attribute.SortOrder,
                RequireConfirm: action.Attribute.RequireConfirm,
                Description: action.Attribute.Description))
            .ToArray();

        return new DashboardPanelDescriptor(
            Id: registration.Id,
            Title: registration.Attribute.Title,
            Version: AppVersion,
            Description: registration.Attribute.Description,
            Order: registration.Attribute.Order,
            Fields: fields,
            Actions: actions);
    }

    private static string GetAppVersion()
    {
        var version = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()).GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static object? FormatValue(object? value, Type valueType)
    {
        if (value is null)
        {
            return null;
        }

        var type = Nullable.GetUnderlyingType(valueType) ?? valueType;
        return type switch
        {
            _ when type == typeof(DateOnly) => ((DateOnly)value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ when type == typeof(DateTime) => ((DateTime)value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ when type == typeof(DateTimeOffset) => ((DateTimeOffset)value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => value
        };
    }

    private static void BindValues(
        DashboardPanelRegistration registration,
        object instance,
        IReadOnlyDictionary<string, JsonElement>? values)
    {
        if (values is null || values.Count == 0)
        {
            return;
        }

        foreach (var field in registration.Fields)
        {
            if (!TryGetValue(values, field.Name, out var element))
            {
                continue;
            }

            object? converted;
            try
            {
                converted = ConvertElement(element, field.ValueType);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Поле '{field.Attribute.Label}' имеет неверный формат: {ex.Message}", ex);
            }

            var isNullable = !field.ValueType.IsValueType || Nullable.GetUnderlyingType(field.ValueType) is not null;
            if (converted is null && !isNullable)
            {
                throw new InvalidOperationException($"Поле '{field.Attribute.Label}' обязательно");
            }

            field.Set(instance, converted);
        }
    }

    private static bool TryGetValue(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        out JsonElement element)
    {
        if (values.TryGetValue(name, out element))
        {
            return true;
        }

        foreach (var pair in values)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                element = pair.Value;
                return true;
            }
        }

        element = default;
        return false;
    }

    private static object? ConvertElement(JsonElement element, Type valueType)
    {
        var type = Nullable.GetUnderlyingType(valueType) ?? valueType;

        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (type != typeof(string) &&
            element.ValueKind == JsonValueKind.String &&
            string.IsNullOrWhiteSpace(element.GetString()))
        {
            return null;
        }

        if (type == typeof(string))
        {
            return element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
        }

        if (type == typeof(bool))
        {
            return element.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? element.GetBoolean()
                : bool.Parse(element.GetString()!);
        }

        if (type == typeof(int))
        {
            return element.ValueKind == JsonValueKind.Number
                ? element.GetInt32()
                : int.Parse(element.GetString()!, CultureInfo.InvariantCulture);
        }

        if (type == typeof(long))
        {
            return element.ValueKind == JsonValueKind.Number
                ? element.GetInt64()
                : long.Parse(element.GetString()!, CultureInfo.InvariantCulture);
        }

        if (type == typeof(short))
        {
            return element.ValueKind == JsonValueKind.Number
                ? element.GetInt16()
                : short.Parse(element.GetString()!, CultureInfo.InvariantCulture);
        }

        if (type == typeof(decimal))
        {
            return element.ValueKind == JsonValueKind.Number
                ? element.GetDecimal()
                : decimal.Parse(element.GetString()!, CultureInfo.InvariantCulture);
        }

        if (type == typeof(double))
        {
            return element.ValueKind == JsonValueKind.Number
                ? element.GetDouble()
                : double.Parse(element.GetString()!, CultureInfo.InvariantCulture);
        }

        if (type == typeof(float))
        {
            return element.ValueKind == JsonValueKind.Number
                ? element.GetSingle()
                : float.Parse(element.GetString()!, CultureInfo.InvariantCulture);
        }

        if (type == typeof(DateOnly))
        {
            return DateOnly.ParseExact(element.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (type == typeof(DateTime))
        {
            return DateTime.Parse(element.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        if (type == typeof(DateTimeOffset))
        {
            return DateTimeOffset.Parse(element.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        return element.Deserialize(type);
    }

    private static async Task<object?> InvokeAsync(object instance, MethodInfo method, CancellationToken cancellationToken)
    {
        var parameters = method.GetParameters();
        var arguments = parameters.Length == 1 ? new object?[] { cancellationToken } : null;
        var result = method.Invoke(instance, arguments);
        return await AwaitResultAsync(result).ConfigureAwait(false);
    }

    private static async Task<object?> AwaitResultAsync(object? result)
    {
        switch (result)
        {
            case null:
                return null;

            case Task task:
                await task.ConfigureAwait(false);
                return task.GetType().IsGenericType
                    ? task.GetType().GetProperty("Result")?.GetValue(task)
                    : null;

            case ValueTask valueTask:
                await valueTask.ConfigureAwait(false);
                return null;

            default:
                var resultType = result.GetType();
                if (resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(ValueTask<>))
                {
                    var task = (Task)resultType.GetMethod("AsTask")!.Invoke(result, null)!;
                    await task.ConfigureAwait(false);
                    return task.GetType().GetProperty("Result")?.GetValue(task);
                }

                return result;
        }
    }
}
