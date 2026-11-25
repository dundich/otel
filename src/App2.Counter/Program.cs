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
builder.Services.AddSingleton<CounterService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CounterService>());

// Регистрация панелей через билдер: DI + endpoints одним вызовом.
builder.AddDashboardPanels(panels => panels
    .Panel<CounterPanel>("Счётчик app2", order: 10, description: "Настройки счётчика app2")
        .Input(p => p.Step, "Шаг", DashboardInputType.Int, sortOrder: 1)
        .Input(p => p.Enabled, "Активно", DashboardInputType.Bool, sortOrder: 2)
        .Action(p => p.Apply(), "Применить", sortOrder: 1)
        .Action(p => p.Reset(), "Сбросить", sortOrder: 2, requireConfirm: true,
            description: "Обнуляет значение счётчика app2"));

builder.AddDashboardRegistration();

var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new { status = "healthy", service = serviceName }));

app.MapGet("/api/state", (CounterService counter) => Results.Ok(new
{
    count = counter.Count,
    step = counter.Step,
    enabled = counter.Enabled
}));

app.Run();
