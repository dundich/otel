using System.Diagnostics.Metrics;

namespace App2.Counter;

/// <summary>
/// Счётчик app2: +шаг раз в секунду, экспортируется как OpenTelemetry-метрика.
/// Состоянием управляет панель «Счётчик app2».
/// </summary>
public sealed class CounterService : BackgroundService
{
    private readonly ILogger<CounterService> _logger;
    private readonly Counter<long> _increments;

    private long _count;
    private long _step = 1;
    private int _enabled = 1;

    public CounterService(ILogger<CounterService> logger)
    {
        _logger = logger;

        var meter = new Meter("OtelDemo.App2", "1.0.0");
        _increments = meter.CreateCounter<long>(
            "app2.counter.increments",
            unit: "{tick}",
            description: "Number of one-second counter increments.");
        meter.CreateObservableGauge(
            "app2.counter.value",
            () => Interlocked.Read(ref _count),
            unit: "{count}",
            description: "Current value of the app2 counter.");
    }

    public long Count => Interlocked.Read(ref _count);
    public long Step => Interlocked.Read(ref _step);
    public bool Enabled => Volatile.Read(ref _enabled) == 1;

    public void SetStep(long step) => Interlocked.Exchange(ref _step, Math.Clamp(step, 0, 1000));
    public void SetEnabled(bool enabled) => Volatile.Write(ref _enabled, enabled ? 1 : 0);
    public void Reset() => Interlocked.Exchange(ref _count, 0);

    /// <summary>Ручной инкремент (кнопка «Применить») на <paramref name="delta"/>.</summary>
    public long Add(long delta)
    {
        var value = Interlocked.Add(ref _count, delta);
        _increments.Add(1);
        return value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🚀 Counter started: +{Step} every second", Step);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!Enabled)
            {
                continue;
            }

            var value = Interlocked.Add(ref _count, Step);
            _increments.Add(1);

            // Keep container logs useful without flooding them every second.
            if (value % 10 == 0)
            {
                _logger.LogInformation("🔢 COUNTER TICK value={Count} step={Step}", value, Step);
            }
        }
    }
}
