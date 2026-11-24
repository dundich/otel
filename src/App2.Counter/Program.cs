using System.Text.Json;
using App2.Counter;
using Dashboard.Client;

var builder = WebApplication.CreateBuilder(args);

builder.AddDemoObservability("app2", "OtelDemo.App2");
builder.Services.AddDashboardPanels(typeof(Program).Assembly);

var app1BaseUrl = builder.Configuration["App1:BaseUrl"] ?? "http://localhost:8081";
builder.Services.AddHttpClient("app1", client =>
{
    client.BaseAddress = new Uri(app1BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(5);
});

builder.Services.AddHttpClient("dashboard-sources", client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddSingleton<DashboardSourceRegistry>();

builder.Services.AddSingleton<CounterService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CounterService>());

var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new { status = "healthy", service = "app2" }));

// Dashboard.Client endpoints are registered even though app2 has no panels itself.
app.MapDashboardPanels();

app.MapGet("/api/panels", async (
    DashboardSourceRegistry sourceRegistry,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var sources = new List<DashboardSourcePanels>();
    var errors = new List<string>();

    foreach (var source in sourceRegistry.Sources)
    {
        try
        {
            sources.Add(await sourceRegistry.GetPanelsAsync(source, cancellationToken));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Dashboard source {Source} is unavailable", source.Name);
            errors.Add($"{source.Name}: {ex.Message}");
        }
    }

    return Results.Ok(new { sources, errors });
});

app.MapPost("/api/panels/{source}/{panelId}/actions/{actionName}", async (
    string source,
    string panelId,
    string actionName,
    DashboardActionRequest? request,
    DashboardPanelRegistry registry,
    DashboardSourceRegistry sourceRegistry,
    CancellationToken cancellationToken) =>
{
    if (string.Equals(source, "app2", StringComparison.OrdinalIgnoreCase))
    {
        var selfResult = await registry.ExecuteAsync(panelId, actionName, request?.Values, cancellationToken);
        return selfResult is null
            ? Results.NotFound(new { error = $"Panel '{panelId}' or action '{actionName}' not found." })
            : Results.Ok(selfResult);
    }

    var execution = await sourceRegistry.ExecuteAsync(
        source, panelId, actionName, request, cancellationToken);

    return execution.Result is null
        ? Results.Problem(
            title: $"Dashboard source '{source}' failed",
            statusCode: execution.StatusCode)
        : Results.Json(execution.Result, statusCode: execution.StatusCode);
});

app.MapGet("/api/state", (CounterService counter) => Results.Ok(new
{
    count = counter.Count,
    lastSentCount = counter.LastSentCount,
    lastSentAt = counter.LastSentAt,
    recentSends = counter.RecentSends
}));

app.MapPost("/api/send", async (
    SendRequest? request,
    CounterService counter,
    IHttpClientFactory httpClientFactory,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var count = request?.Count ?? counter.Count;
    if (count < 0)
    {
        return Results.BadRequest(new { error = "count must be >= 0" });
    }

    var client = httpClientFactory.CreateClient("app1");

    try
    {
        using var response = await client.PostAsJsonAsync(
            "/api/messages",
            new { count, source = "app2" },
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "SEND FAILED count={Count} status={Status} response={Response}",
                count, (int)response.StatusCode, body);

            return Results.Problem(
                title: "app1 rejected the message",
                detail: body,
                statusCode: (int)response.StatusCode);
        }

        var record = new SendRecord(count, (int)response.StatusCode, body, DateTimeOffset.UtcNow);
        counter.MarkSent(count, record);

        logger.LogInformation(
            "SENT TO APP1 count={Count} status={Status} response={Response}",
            count, (int)response.StatusCode, body);

        return Results.Ok(new
        {
            sent = count,
            status = (int)response.StatusCode,
            app1Response = body
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "SEND FAILED count={Count} error={Error}", count, ex.Message);
        return Results.Problem($"Could not reach app1: {ex.Message}");
    }
});

app.Run();

internal sealed record SendRequest(long? Count);

