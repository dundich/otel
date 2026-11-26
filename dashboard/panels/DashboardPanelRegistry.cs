using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace RaMonitor.Panels;

/// <summary>
/// Registration of one panel: type + metadata collected by
/// <see cref="DashboardPanelBuilder"/> (explicit) or from attributes
/// (<see cref="DashboardPanelBuilder.ScanAssembly"/> sugar).
/// </summary>
internal sealed class DashboardPanelRegistration
{
    private readonly List<FieldBinding> _fields = [];
    private readonly List<ActionBinding> _actions = [];
    private readonly List<ElementBinding> _elements = [];

    public DashboardPanelRegistration(
        Type panelType,
        string title,
        int order,
        string? id,
        string? description,
        DashboardPanelKind kind = DashboardPanelKind.Panel)
    {
        if (!typeof(IDashboardPanel).IsAssignableFrom(panelType))
        {
            throw new InvalidOperationException(
                $"Type '{panelType.FullName}' must implement {nameof(IDashboardPanel)}.");
        }

        PanelType = panelType;
        Title = title;
        Order = order;
        Id = string.IsNullOrWhiteSpace(id) ? panelType.Name : id;
        Description = description;
        Kind = kind;
    }

    public Type PanelType { get; }
    public string Id { get; }
    public string Title { get; }
    public int Order { get; }
    public string? Description { get; }
    public DashboardPanelKind Kind { get; }
    public IReadOnlyList<FieldBinding> Fields => _fields;
    public IReadOnlyList<ActionBinding> Actions => _actions;
    public IReadOnlyList<ElementBinding> Elements => _elements;
    public SemaphoreSlim Gate { get; } = new(1, 1);

    public void AddField(FieldBinding field)
    {
        if (_fields.Any(existing => string.Equals(existing.Name, field.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Panel '{Id}' already has an input '{field.Name}'.");
        }

        _fields.Add(field);
    }

    public void AddAction(ActionBinding action)
    {
        if (_actions.Any(existing => string.Equals(existing.Name, action.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Panel '{Id}' has duplicate action '{action.Name}'. Action names must be unique.");
        }

        _actions.Add(action);
    }

    public void AddElement(ElementBinding element) => _elements.Add(element);

    /// <summary>
    /// Attribute sugar: builds a registration from [DashboardPanel],
    /// [DashboardInput] and [DashboardAction].
    /// </summary>
    public static DashboardPanelRegistration FromAttributes(Type panelType)
    {
        var attribute = panelType.GetCustomAttribute<DashboardPanelAttribute>()
            ?? throw new InvalidOperationException(
                $"Type '{panelType.FullName}' has no {nameof(DashboardPanelAttribute)}.");

        var registration = new DashboardPanelRegistration(
            panelType,
            attribute.Title,
            attribute.Order,
            attribute.Id,
            attribute.Description,
            attribute.Kind);

        var fields = panelType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => (Member: (MemberInfo)property, Attribute: property.GetCustomAttribute<DashboardInputAttribute>()))
            .Concat(panelType.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Select(field => (Member: (MemberInfo)field, Attribute: field.GetCustomAttribute<DashboardInputAttribute>())))
            .Where(x => x.Attribute is not null)
            .OrderBy(x => x.Attribute!.SortOrder)
            .ThenBy(x => x.Member.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var (member, input) in fields)
        {
            registration.AddField(new FieldBinding(
                member,
                input!.Label,
                input.Type,
                input.SortOrder,
                input.Placeholder,
                input.Required));
        }

        // Metadata only: the action name is the method name and is dispatched
        // by the panel itself through IDashboardPanel.ExecuteAsync.
        var actions = panelType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(method => (Method: method, Attribute: method.GetCustomAttribute<DashboardActionAttribute>()))
            .Where(x => x.Attribute is not null)
            .OrderBy(x => x.Attribute!.SortOrder)
            .ThenBy(x => x.Method.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var (method, action) in actions)
        {
            registration.AddAction(new ActionBinding(
                method.Name,
                action!.Label,
                action.SortOrder,
                action.RequireConfirm,
                action.Description));
        }

        return registration;
    }

    public sealed class FieldBinding
    {
        public FieldBinding(
            MemberInfo member,
            string label,
            DashboardInputType type,
            int sortOrder,
            string? placeholder,
            bool required)
        {
            Member = member;
            Label = label;
            SortOrder = sortOrder;
            Placeholder = placeholder;
            Required = required;

            ValueType = member switch
            {
                PropertyInfo property => property.PropertyType,
                FieldInfo field => field.FieldType,
                _ => throw new InvalidOperationException($"Unsupported member '{member.Name}'.")
            };

            InputType = ResolveInputType(ValueType, type).ToString().ToLowerInvariant();

            switch (member)
            {
                case PropertyInfo property when property.GetMethod is not null && IsReadOnly:
                    // Read-only display: values are only read from the app, never
                    // posted back, so a setter is optional.
                    Get = CompileGetter(property);
                    Set = null;
                    break;
                case PropertyInfo property when property.GetMethod is not null && property.SetMethod is not null:
                    Get = CompileGetter(property);
                    Set = CompileSetter(property);
                    break;
                case FieldInfo field:
                    Get = CompileGetter(field);
                    Set = CompileSetter(field);
                    break;
                case PropertyInfo property:
                    throw new InvalidOperationException(
                        $"Input '{property.Name}' must have a public getter and setter " +
                        $"(use {nameof(DashboardInputType)}.{nameof(DashboardInputType.ReadOnly)} for display-only values).");
                default:
                    throw new InvalidOperationException($"Unsupported member '{member.Name}'.");
            }
        }

        private bool IsReadOnly => InputType == nameof(DashboardInputType.ReadOnly).ToLowerInvariant();

        // Reflection is used once here, at registration: the accessors are
        // compiled into delegates so requests never touch PropertyInfo.GetValue/SetValue.
        private static Func<object, object?> CompileGetter(PropertyInfo property)
        {
            var instance = Expression.Parameter(typeof(object), "instance");
            var access = Expression.Property(Expression.Convert(instance, property.DeclaringType!), property);
            return Expression.Lambda<Func<object, object?>>(Expression.Convert(access, typeof(object)), instance).Compile();
        }

        private static Func<object, object?> CompileGetter(FieldInfo field)
        {
            var instance = Expression.Parameter(typeof(object), "instance");
            var access = Expression.Field(Expression.Convert(instance, field.DeclaringType!), field);
            return Expression.Lambda<Func<object, object?>>(Expression.Convert(access, typeof(object)), instance).Compile();
        }

        private static Action<object, object?> CompileSetter(PropertyInfo property)
        {
            var instance = Expression.Parameter(typeof(object), "instance");
            var value = Expression.Parameter(typeof(object), "value");
            var assign = Expression.Assign(
                Expression.Property(Expression.Convert(instance, property.DeclaringType!), property),
                Expression.Convert(value, property.PropertyType));
            return Expression.Lambda<Action<object, object?>>(assign, instance, value).Compile();
        }

        private static Action<object, object?> CompileSetter(FieldInfo field)
        {
            var instance = Expression.Parameter(typeof(object), "instance");
            var value = Expression.Parameter(typeof(object), "value");
            var assign = Expression.Assign(
                Expression.Field(Expression.Convert(instance, field.DeclaringType!), field),
                Expression.Convert(value, field.FieldType));
            return Expression.Lambda<Action<object, object?>>(assign, instance, value).Compile();
        }

        public MemberInfo Member { get; }
        public string Label { get; }
        public int SortOrder { get; }
        public string? Placeholder { get; }
        public bool Required { get; }
        public Type ValueType { get; }
        public string Name => Member.Name;
        public string InputType { get; }
        public Func<object, object?> Get { get; }
        public Action<object, object?>? Set { get; }

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

    public sealed record ActionBinding(
        string Name,
        string Label,
        int SortOrder,
        bool RequireConfirm,
        string? Description);

    public sealed record ElementBinding(
        string Type,
        string Text,
        int Order);
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
            .OrderBy(x => x.Order)
            .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var duplicate = _panels
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Duplicate dashboard panel id '{duplicate.Key}'. Give each panel a unique id.");
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

                // Вызов RenderAsync — при появлении панели (GET /dashboard/panels),
                // не по нажатию кнопки: значения полей пересчитываются перед
                // сериализацией дескриптора в дашборд.
                await ((IDashboardPanel)instance).RenderAsync(cancellationToken).ConfigureAwait(false);

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
            string.Equals(x.Name, actionName, StringComparison.OrdinalIgnoreCase));

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
                // Сначала значения из формы → свойства панели (скомпилированные
                // сеттеры), ПОТОМ ExecuteAsync — это путь нажатия кнопки действия
                // в UI, а не путь обновления панели.
                BindValues(registration, instance, values);
                var data = await ((IDashboardPanel)instance)
                    .ExecuteAsync(action.Name, cancellationToken).ConfigureAwait(false);

                var content = data switch
                {
                    DashboardContent value => value,
                    null => DashboardContent.Text($"{action.Label}: готово"),
                    string text => DashboardContent.Text(text),
                    _ => DashboardContent.Json(JsonSerializer.Serialize(data))
                };

                return new DashboardActionResult(
                    Success: true,
                    Content: content,
                    Panel: BuildDescriptor(registration, instance),
                    Data: data);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Dashboard action {PanelId}/{ActionName} failed", registration.Id, action.Name);

                return new DashboardActionResult(
                    Success: false,
                    Content: DashboardContent.Text(ex.Message),
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
            .OrderBy(field => field.SortOrder)
            .ThenBy(field => field.Name, StringComparer.OrdinalIgnoreCase)
            .Select(field => new DashboardFieldDescriptor(
                Name: field.Name,
                Label: field.Label,
                InputType: field.InputType,
                Order: field.SortOrder,
                Placeholder: field.Placeholder,
                Required: field.Required,
                Value: FormatValue(field.Get(instance), field.ValueType)))
            .ToArray();

        var actions = registration.Actions
            .OrderBy(action => action.SortOrder)
            .ThenBy(action => action.Name, StringComparer.OrdinalIgnoreCase)
            .Select(action => new DashboardActionDescriptor(
                Name: action.Name,
                Label: action.Label,
                Order: action.SortOrder,
                RequireConfirm: action.RequireConfirm,
                Description: action.Description))
            .ToArray();

        var elements = registration.Elements
            .OrderBy(element => element.Order)
            .Select(element => new DashboardElementDescriptor(
                Type: element.Type,
                Text: element.Text,
                Order: element.Order))
            .ToArray();

        return new DashboardPanelDescriptor(
            Id: registration.Id,
            Title: registration.Title,
            Version: AppVersion,
            Description: registration.Description,
            Order: registration.Order,
            Fields: fields,
            Actions: actions,
            Elements: elements,
            Kind: DashboardPanelKinds.ToWireValue(registration.Kind));
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
            if (field.Set is null)
            {
                // Read-only display field: nothing to bind back.
                continue;
            }

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
                    $"Поле '{field.Label}' имеет неверный формат: {ex.Message}", ex);
            }

            var isNullable = !field.ValueType.IsValueType || Nullable.GetUnderlyingType(field.ValueType) is not null;
            if (converted is null && !isNullable)
            {
                throw new InvalidOperationException($"Поле '{field.Label}' обязательно");
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
}
