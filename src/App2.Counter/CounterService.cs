using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace App2.Counter;

/// <summary>
/// Increments a counter once per second and exposes it as an OpenTelemetry metric.
/// The same singleton is used by the HTTP endpoint that sends the current value to app1.
/// </summary>
public sealed class CounterService : BackgroundService
{
    private readonly ILogger<CounterService> _logger;
    private readonly Counter<long> _increments;
    private readonly Counter<long> _sentMessages;
    private readonly Histogram<long> _sentValues;
    private readonly ConcurrentQueue<SendRecord> _recentSends = new();

    private long _count;
    private long _lastSentCount = -1;
    private long _lastSentAtTicks;
    private int _step = 1;
    private int _enabled = 1;

    public CounterService(ILogger<CounterService> logger)
    {
        _logger = logger;

        var meter = new Meter("OtelDemo.App2", "1.0.0");
        _increments = meter.CreateCounter<long>(
            "app2.counter.increments",
            unit: "{tick}",
            description: "Number of one-second counter increments.");
        _sentMessages = meter.CreateCounter<long>(
            "app2.messages.sent",
            unit: "{message}",
            description: "Messages successfully sent from app2 to app1.");
        _sentValues = meter.CreateHistogram<long>(
            "app2.messages.sent.value",
            unit: "{count}",
            description: "Counter values sent from app2 to app1.");
        meter.CreateObservableGauge(
            "app2.counter.value",
            () => Interlocked.Read(ref _count),
            unit: "{count}",
            description: "Current value of the app2 counter.");
    }

    public long Count => Interlocked.Read(ref _count);
    public int Step => Volatile.Read(ref _step);
    public bool Enabled => Volatile.Read(ref _enabled) == 1;

    public void SetStep(int step) => Volatile.Write(ref _step, Math.Clamp(step, 0, 1000));
    public void SetEnabled(bool enabled) => Volatile.Write(ref _enabled, enabled ? 1 : 0);
    public long? LastSentCount
    {
        get
        {
            var value = Interlocked.Read(ref _lastSentCount);
            return value < 0 ? null : value;
        }
    }

    public DateTimeOffset? LastSentAt
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastSentAtTicks);
            return ticks <= 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    public IReadOnlyList<SendRecord> RecentSends => _recentSends.Reverse().ToArray();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Counter started: +1 every second");

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
                _logger.LogInformation("COUNTER TICK value={Count} step={Step}", value, Step);
            }
        }
    }

    public void MarkSent(long count, SendRecord record)
    {
        Interlocked.Exchange(ref _lastSentCount, count);
        Interlocked.Exchange(ref _lastSentAtTicks, DateTimeOffset.UtcNow.UtcTicks);

        _sentMessages.Add(1);
        _sentValues.Record(count);

        _recentSends.Enqueue(record);
        while (_recentSends.Count > 20 && _recentSends.TryDequeue(out _))
        {
        }
    }
}

public sealed record SendRecord(
    long Count,
    int StatusCode,
    string? Response,
    DateTimeOffset SentAt);
