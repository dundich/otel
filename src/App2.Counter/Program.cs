using App2.Counter;
using RaMonitor.Panels;

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
    .Panel<MigrationPanel>("Миграции", order: 1,
        description: "Пример отчёта по миграциям PostgreSQL и Elasticsearch")
        .Warn("ВНИМАНИЕ: миграция меняет схему PostgreSQL и mapping Elasticsearch. Сделайте резервную копию и согласуйте окно работ.", sortOrder: 0)
        .Info("Кнопка «Миграция» формирует Markdown-отчёт со списком объектов; команды не выполняются.", sortOrder: 1)
        .Action(p => p.Report(), "Миграция", sortOrder: 1,
            description: "Открыть Markdown-отчёт со списком миграций")
    .Panel<CounterPanel>("Счётчик app2", order: 10, description: "Настройки счётчика app2")
        .Label("Живой счётчик приложения app2", sortOrder: 0)
        .Info("Значение обновляется каждую секунду.", sortOrder: 1)
        .Note("Настройки применяются к работающему приложению.", sortOrder: 2)
        .Warn("«Сбросить» обнуляет счётчик после подтверждения.", sortOrder: 3)
        .Input(p => p.Step, "Шаг", DashboardInputType.Int, sortOrder: 1)
        .Input(p => p.Enabled, "Активно", DashboardInputType.Bool, sortOrder: 2)
        .Input(p => p.Count, "Значение", DashboardInputType.ReadOnly, sortOrder: 3)
        .Action(p => p.Apply(), "Применить", sortOrder: 1)
        .Action(p => p.Reset(), "Сбросить", sortOrder: 2, requireConfirm: true,
            description: "Обнуляет значение счётчика app2")
        .Action(p => p.Report(), "Отчёт (MD)", sortOrder: 3,
            description: "Возвращает Markdown-отчёт о состоянии счётчика")
    .SearchPanel<CallSearchPanel>("Звонки", order: 20,
        description: "Поиск телефонных звонков по номеру")
        .Info("Можно ввести часть номера. Enter в поле запускает поиск.", sortOrder: 0)
        .Input(p => p.Phone, "Телефон", DashboardInputType.Text, sortOrder: 1,
            placeholder: "+7 999 ...")
        .Action(p => p.Find(), "Найти", sortOrder: 1)
    .SearchPanel<ImportSearchPanel>("Импорт", order: 21,
        description: "Журнал импорта за выбранный период")
        .Label("Период импорта", sortOrder: 0)
        .Input(p => p.DateFrom, "Дата с", DashboardInputType.Date, sortOrder: 1)
        .Input(p => p.DateTo, "Дата по", DashboardInputType.Date, sortOrder: 2)
        .Action(p => p.Find(), "Найти", sortOrder: 1)
    .SearchPanel<InvoiceSearchPanel>("Накладная", order: 22,
        description: "Поиск накладной по номеру")
        .Input(p => p.Number, "Номер", DashboardInputType.Text, sortOrder: 1,
            placeholder: "НК-1042", required: true)
        .Action(p => p.Find(), "Найти", sortOrder: 1));

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
