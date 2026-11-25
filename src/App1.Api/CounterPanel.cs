using Dashboard.Client;

namespace App1.Api;

/// <summary>
/// Панель собственного счётчика app1. Здесь метаданные заданы атрибутами —
/// это пример «сахара»: Program.cs регистрирует панель через
/// builder.AddDashboardPanels&lt;Program&gt;() и скан сборки.
/// </summary>
[DashboardPanel("Счётчик app1", order: 30,
    Description = "Собственный счётчик app1: шаг, пауза и сброс")]
public sealed class CounterPanel(CounterService counter, ILogger<CounterPanel> logger) : IDashboardPanel
{
    [DashboardInput("Шаг", DashboardInputType.Int, sortOrder: 1)]
    public long Step { get; set; } = 1;

    [DashboardInput("Активно", DashboardInputType.Bool, sortOrder: 2)]
    public bool Enabled { get; set; } = true;

    /// <summary>Текущее значение счётчика — только для показа в панели.</summary>
    [DashboardInput("Значение", DashboardInputType.ReadOnly, sortOrder: 3)]
    public long Count { get; set; }

    // ВЫЗЫВАЕТСЯ ПРИ ПОЯВЛЕНИИ ПАНЕЛИ (каждый GET /dashboard/panels):
    // читает актуальные Step/Enabled из собственного CounterService.
    public Task RenderAsync(CancellationToken cancellationToken = default)
    {
        Step = counter.Step;
        Enabled = counter.Enabled;
        Count = counter.Count;
        return Task.CompletedTask;
    }

    // ВЫЗЫВАЕТСЯ ПО КЛАВЕ «Применить»: значения формы уже в свойствах.
    // Применяет настройки и один раз увеличивает значение на шаг.
    [DashboardAction("Применить", sortOrder: 1)]
    public Task<string> Apply()
    {
        counter.SetStep(Step);
        counter.SetEnabled(Enabled);
        Count = counter.Add(Step);

        logger.LogInformation(
            "APP1 COUNTER CONFIG APPLIED step={Step} enabled={Enabled} value={Count}",
            counter.Step, counter.Enabled, counter.Count);

        return Task.FromResult(
            $"Счётчик app1: шаг={counter.Step}, активно={counter.Enabled}, значение={counter.Count}");
    }

    // RequireConfirm=true → в дашборде перед действием будет confirm().
    [DashboardAction("Сбросить", sortOrder: 2, RequireConfirm = true,
        Description = "Обнуляет значение счётчика app1")]
    public Task<string> Reset()
    {
        counter.Reset();
        Count = counter.Count;
        logger.LogWarning("APP1 COUNTER RESET value={Count}", counter.Count);
        return Task.FromResult($"Счётчик app1 сброшен: значение={counter.Count}");
    }

    // ВЫЗЫВАЕТСЯ ПО КЛАВЕ действия: клик → SignalR circuit →
    // POST {source}/dashboard/panels/{panel}/actions/{actionName} → сюда.
    public async Task<object?> ExecuteAsync(string actionName, CancellationToken cancellationToken = default)
        => actionName switch
        {
            nameof(Apply) => await Apply().ConfigureAwait(false),
            nameof(Reset) => await Reset().ConfigureAwait(false),
            _ => throw new InvalidOperationException(
                $"Action '{actionName}' is not handled by {nameof(CounterPanel)}.")
        };
}
