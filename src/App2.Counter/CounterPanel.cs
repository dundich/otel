using Dashboard.Client;

namespace App2.Counter;

/// <summary>
/// Панель счётчика app2. Управляет живым singleton-сервисом CounterService:
/// читает его состояние в RenderAsync и меняет его в действиях.
/// </summary>
[DashboardPanel("Счётчик app2", order: 10, Description = "Настройки счётчика app2")]
public sealed class CounterPanel(CounterService counter, ILogger<CounterPanel> logger) : IDashboardPanel
{
    [DashboardInput("Шаг", DashboardInputType.Int, sortOrder: 1)]
    public int Step { get; set; } = 1;

    [DashboardInput("Активно", DashboardInputType.Bool, sortOrder: 2)]
    public bool Enabled { get; set; } = true;

    // ВЫЗЫВАЕТСЯ ПРИ ПОЯВЛЕНИИ ПАНЕЛИ, а не по нажатию кнопок: каждый
    // GET /dashboard/panels (страница «Управление», «Обновить»). Здесь
    // актуальное состояние CounterService записывается в свойства формы,
    // чтобы в дашборд ушли живые значения, а не значения по умолчанию.
    public Task RenderAsync(CancellationToken cancellationToken = default)
    {
        Step = (int)counter.Step;
        Enabled = counter.Enabled;
        return Task.CompletedTask;
    }

    // Метаданные кнопки в UI; имя метода = имя действия для диспетчеризации
    // в ExecuteAsync ниже.
    [DashboardAction("Применить", sortOrder: 1)]
    public Task<string> Apply()
    {
        counter.SetStep(Step);
        counter.SetEnabled(Enabled);

        logger.LogInformation(
            "COUNTER CONFIG APPLIED step={Step} enabled={Enabled} value={Count}",
            counter.Step, counter.Enabled, counter.Count);

        return Task.FromResult(
            $"Счётчик: шаг={counter.Step}, активно={counter.Enabled}, значение={counter.Count}");
    }

    // RequireConfirm=true — в дашборде перед действием покажется confirm().
    [DashboardAction("Сбросить", sortOrder: 2, RequireConfirm = true,
        Description = "Обнуляет значение счётчика app2")]
    public Task<string> Reset()
    {
        counter.Reset();
        logger.LogWarning("COUNTER RESET value={Count}", counter.Count);
        return Task.FromResult($"Счётчик сброшен: значение={counter.Count}");
    }

    // ВЫЗЫВАЕТСЯ ПО КЛАВЕ действия: клик → SignalR circuit →
    // POST {source}/dashboard/panels/{panel}/actions/{actionName} (source = имя
    // этого сервиса, special-case в Program.cs → локальный registry) → сюда.
    public async Task<object?> ExecuteAsync(string actionName, CancellationToken cancellationToken = default)
        => actionName switch
        {
            nameof(Apply) => await Apply().ConfigureAwait(false),
            nameof(Reset) => await Reset().ConfigureAwait(false),
            _ => throw new InvalidOperationException(
                $"Action '{actionName}' is not handled by {nameof(CounterPanel)}.")
        };
}
