namespace App1.Api;

/// <summary>
/// Собственный счётчик app1 — аналог CounterService в app2, но со своим
/// независимым состоянием. Тикает раз в секунду, шаг и пауза управляются
/// из панели «Счётчик app1».
/// </summary>
public sealed class CounterService(ILogger<CounterService> logger) : BackgroundService
{
    private long _count;
    private long _step = 1;
    private int _enabled = 1;

    public long Count => Interlocked.Read(ref _count);
    public long Step => Interlocked.Read(ref _step);
    public bool Enabled => Volatile.Read(ref _enabled) == 1;

    public void SetStep(long step) => Interlocked.Exchange(ref _step, Math.Clamp(step, 0, 1000));
    public void SetEnabled(bool enabled) => Volatile.Write(ref _enabled, enabled ? 1 : 0);
    public void Reset() => Interlocked.Exchange(ref _count, 0);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("app1 counter started: step={Step} every second", Step);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!Enabled)
            {
                continue;
            }

            var value = Interlocked.Add(ref _count, Step);

            // Не засоряем логи каждую секунду — только круглые значения.
            if (value % 10 == 0)
            {
                logger.LogInformation("APP1 COUNTER TICK value={Count} step={Step}", value, Step);
            }
        }
    }
}
