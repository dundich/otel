using Dashboard.Client;

namespace App2.Counter;

/// <summary>
/// Панель счётчика app2. Метаданные заданы в Program.cs через билдер;
/// панель управляет живым singleton-сервисом CounterService.
/// </summary>
public sealed class CounterPanel(CounterService counter, ILogger<CounterPanel> logger) : IDashboardPanel
{
    public long Step { get; set; } = 1;

    public bool Enabled { get; set; } = true;

    // ВЫЗЫВАЕТСЯ ПРИ ПОЯВЛЕНИИ ПАНЕЛИ, а не по нажатию кнопок: каждый
    // GET /dashboard/panels (страница «Управление», «Обновить»). Здесь
    // актуальное состояние CounterService записывается в свойства формы.
    public Task RenderAsync(CancellationToken cancellationToken = default)
    {
        Step = counter.Step;
        Enabled = counter.Enabled;
        return Task.CompletedTask;
    }

    // ВЫЗЫВАЕТСЯ ПО КЛАВЕ «Применить»: значения формы уже в свойствах.
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

    // RequireConfirm задан в билдере → в дашборде перед действием будет confirm().
    public Task<string> Reset()
    {
        counter.Reset();
        logger.LogWarning("COUNTER RESET value={Count}", counter.Count);
        return Task.FromResult($"Счётчик сброшен: значение={counter.Count}");
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
