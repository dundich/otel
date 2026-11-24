using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using App1.Api;
using Dashboard.Client;

var builder = WebApplication.CreateBuilder(args);

builder.AddDemoObservability("app1", "OtelDemo.App1");
builder.Services.AddSingleton<MessageStore>();
builder.Services.AddDashboardPanel<App1DemoPanel>();

if (builder.Configuration.GetValue<bool>("Standalone:Enabled"))
{
    builder.Services.AddDashboardPanel<StandalonePanel>();
}

var app = builder.Build();

var meter = new Meter("OtelDemo.App1", "1.0.0");
var receivedCounter = meter.CreateCounter<long>(
    "app1.messages.received",
    unit: "{message}",
    description: "Number of messages received from app2.");
var receivedValue = meter.CreateHistogram<long>(
    "app1.messages.value",
    unit: "{count}",
    description: "Counter value carried by each received message.");

app.MapGet("/", () => Results.Ok(new
{
    service = "app1",
    status = "ok",
    endpoints = new[] { "POST /api/messages", "GET /api/messages", "GET /healthz" }
}));

app.MapGet("/healthz", () => Results.Ok(new { status = "healthy", service = "app1" }));

app.MapDashboardPanels();

app.MapGet("/api/messages", (MessageStore store) => Results.Ok(store.Snapshot()));

app.MapPost("/api/messages", (MessageRequest request, MessageStore store, ILogger<Program> logger) =>
{
    if (request.Count < 0)
    {
        logger.LogWarning("Rejected message with negative count {Count}", request.Count);
        return Results.BadRequest(new { error = "count must be >= 0" });
    }

    var message = new ReceivedMessage(
        Id: Guid.NewGuid(),
        Count: request.Count,
        Source: string.IsNullOrWhiteSpace(request.Source) ? "app2" : request.Source!,
        ReceivedAt: DateTimeOffset.UtcNow);

    store.Add(message);

    receivedCounter.Add(1, new KeyValuePair<string, object?>("source", message.Source));
    receivedValue.Record(message.Count, new KeyValuePair<string, object?>("source", message.Source));

    // This is the line that must be visible in both container logs and the Aspire Dashboard.
    logger.LogInformation(
        "MESSAGE RECEIVED id={MessageId} count={Count} source={Source}",
        message.Id, message.Count, message.Source);

    return Results.Accepted($"/api/messages/{message.Id}", message);
});

app.Run();

internal sealed record MessageRequest(long Count, string? Source);

internal sealed record ReceivedMessage(Guid Id, long Count, string Source, DateTimeOffset ReceivedAt);

internal sealed class MessageStore
{
    private const int MaxMessages = 100;
    private readonly ConcurrentQueue<ReceivedMessage> _messages = new();

    public void Add(ReceivedMessage message)
    {
        _messages.Enqueue(message);
        while (_messages.Count > MaxMessages && _messages.TryDequeue(out _))
        {
        }
    }

    public IReadOnlyList<ReceivedMessage> Snapshot()
        => _messages.Reverse().ToArray();
}
