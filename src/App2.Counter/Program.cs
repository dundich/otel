using App2.Counter;
using Dashboard.Client;

var builder = WebApplication.CreateBuilder(args);

// Имя источника панелей = имя OTel-сервиса: его видно в левой панели
// дашборда (resource name), поэтому клик по ресурсу и фильтр
// /management/resource/{name} совпадают с источником без хардкода.
// Без OTEL_SERVICE_NAME сервису нечего экспортировать — падаем сразу.
var serviceName = builder.Configuration["OTEL_SERVICE_NAME"]
    ?? throw new InvalidOperationException(
        "OTEL_SERVICE_NAME is not set. It is the OTel service name " +
        "(see compose.yaml).");

builder.AddDemoObservability(serviceName, "OtelDemo.App2");
builder.Services.AddDashboardPanels(typeof(Program).Assembly);

builder.Services.AddSingleton<CounterService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CounterService>());

var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new { status = "healthy", service = serviceName }));

// Dashboard.Client endpoints: app2 serves its own panels.
app.MapDashboardPanels();

app.MapGet("/api/state", (CounterService counter) => Results.Ok(new
{
    count = counter.Count,
    step = counter.Step,
    enabled = counter.Enabled
}));

app.Run();
