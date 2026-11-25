# РА-Монитор

Минимальный пример на **.NET 10**: кастомный **Aspire Dashboard**, собранный из исходников, плюс два приложения с панелями управления и файловым браузером.

Без Aspire AppHost, без nginx и без iframe. Всё поднимается через `docker compose`.

## Что внутри

```text
src/
  Dashboard.Client/     # OTel + SDK панелей: атрибуты, DI, /dashboard/panels
  App1.Api/             # Web API: принимает сообщения, пишет лог, панель app1
  App2.Counter/         # счётчик +1/сек, OTLP, свои панели
dashboard/
  aspire/               # vendored исходники Aspire 17.0.0
  Dockerfile            # сборка кастомного dashboard
logs/                   # корень вкладки «Логи»
compose.yaml
```

Контейнеры:

- `aspire-dashboard` — кастомный dashboard из `dashboard/aspire`;
- `app1`;
- `app2`.

## Быстрый старт

```bash
docker compose up -d --build
```

Открыть:

**http://localhost:18888**

Основные разделы dashboard:

```text
Structured logs   Traces   Metrics   Управление   Логи   Share
```

Полезные адреса:

| Адрес | Что это |
|---|---|
| http://localhost:18888 | Aspire Dashboard |
| http://localhost:18888/structuredlogs | логи |
| http://localhost:18888/traces | трейсы |
| http://localhost:18888/metrics | метрики |
| http://localhost:18888/management | панели приложений |
| http://localhost:18888/logs | файловый браузер логов |
| http://localhost:18888/share | файловый браузер домашней директории |
| http://localhost:8081 | app1 API |
| http://localhost:8082 | app2 API |
| http://localhost:4317 | OTLP/gRPC |

Остановить:

```bash
docker compose down
```

## Как это работает

```text
app1/app2 ── OTLP/gRPC ──→ aspire-dashboard:18889
                                │
                                ▼
                       http://localhost:18888

dashboard /management ──→ app1: /dashboard/panels
                     └──→ app2: /dashboard/panels
```

Дашборд запрашивает панели **на сервере** напрямую у каждого приложения из
`Management__Sources__*`, поэтому proxy и CORS не нужны.

## Панели приложений

`Dashboard.Client` даёт атрибуты:

```csharp
[DashboardPanel("Моя панель", order: 10)]
public sealed class MyPanel(ILogger<MyPanel> logger) : IDashboardPanel
{
    [DashboardInput("Имя", sortOrder: 1)]
    public string Name { get; set; } = "";

    [DashboardInput("Дата рождения", DashboardInputType.Date, sortOrder: 2)]
    public DateOnly BirthDate { get; set; }

    [DashboardInput("Количество", DashboardInputType.Int, sortOrder: 3)]
    public int Count { get; set; }

    [DashboardInput("Активно", DashboardInputType.Bool, sortOrder: 4)]
    public bool Enabled { get; set; }

    // при появлении панели: каждый GET /dashboard/panels
    public Task RenderAsync(CancellationToken ct = default)
    {
        Name = "Ваня";
        return Task.CompletedTask;
    }

    [DashboardAction("Применить")]
    public Task<string> Apply() => Task.FromResult($"Принято: {Name}, {Count}");

    // по нажатию кнопки: значения формы уже записаны в свойства выше
    public async Task<object?> ExecuteAsync(string actionName, CancellationToken ct = default)
        => actionName switch
        {
            nameof(Apply) => await Apply().ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unknown action '{actionName}'.")
        };
}
```

Атрибуты `[DashboardPanel]`/`[DashboardInput]`/`[DashboardAction]` — только
метаданные (заголовок, типы полей, кнопки); читаются рефлексией **один раз при
регистрации**. В рантайме запроса рефлексии нет:

- `RenderAsync()` — вызывается **при появлении панели**: каждый
  `GET /dashboard/panels` (открытие/обновление страницы «Управление»),
  пересчитывает значения полей перед сериализацией в дашборд;
- `ExecuteAsync(actionName)` — вызывается **по нажатию кнопки действия**:
  клик → SignalR circuit → `POST .../actions/{actionName}` → сначала
  `BindValues` записывает значения формы в свойства (скомпилированные
  сеттеры), затем диспетчер `switch (actionName)` вызывает метод с
  `[DashboardAction]` напрямую.

Подключение через DI в приложении:

```csharp
builder.Services.AddDashboardPanel<MyPanel>();
app.MapDashboardPanels();
```

Панель — singleton с обычным constructor injection (`ILogger`, `CounterService`, ...).
Контракт — интерфейс `IDashboardPanel`: `RenderAsync` вызывается при появлении
панели (каждый `GET /dashboard/panels`), `ExecuteAsync` — по нажатию кнопки
действия. Рефлексии в рантайме нет: атрибуты читаются один раз при регистрации,
доступы к свойствам компилируются в делегаты.

### Управление

На странице **«Управление»**:

- фильтр **«Приложение»**: все / настроенные источники (`app2`);
- клик по имени в левом resource pane фильтрует панели по источнику;
- поля `text`, `date`, `int`, `bool`;
- кнопки действий (включая «Применить»);
- версия приложения в заголовке панели: `Счётчик app2 v10.0.2`;
- `Public` адрес приложения;
- результат действия уходит в **колокольчик** Aspire Dashboard (`INotificationService`).

Скриншот: `docs/management.png`.

### API панелей

```text
GET  /dashboard/panels
POST /dashboard/panels/{panelId}/actions/{actionName}
```

Тело действия:

```json
{ "values": { "Name": "Ваня", "Count": 42, "Enabled": true } }
```

SDK записывает значения в свойства (скомпилированные сеттеры), вызывает
`IDashboardPanel.ExecuteAsync` и возвращает полный снимок панели.

### Панели app1

app1 — домен «собственный счётчик», одна панель. Живёт по адресу
`app1:8080/dashboard/panels` и видна в Management, потому что app1 указан
в `Management__Sources__0__*` у дашборда.

- **«Счётчик app1»** (`CounterPanel` + свой `CounterService`) — независимый
  счётчик этого приложения: `Step`/`Enabled`, «Применить», «Сбросить»
  с confirm. Тикает раз в секунду, как и счётчик app2.

Журнал сообщений (`MessageStore`, `POST/GET /api/messages`) остаётся API
приложения, отдельной панели у него больше нет.

### Панели app2

app2 — обычное приложение со своими панелями, без роли агрегатора. Панель
**«Счётчик app2»** (`CounterPanel`) управляет живым `CounterService`:

- `RenderAsync` — при появлении панели читает `Step`/`Enabled` из сервиса;
- **«Применить»** — `SetStep`/`SetEnabled`;
- **«Сбросить»** — `counter.Reset()`, с `RequireConfirm` (confirm в UI).

Агрегацию панелей делает **сам дашборд** (домен
`dashboard/aspire/src/Aspire.Dashboard/Management/`): он читает
`Management:Sources` и ходит в каждое приложение напрямую. Имена источников
совпадают с OTel-именами (`OTEL_SERVICE_NAME`), поэтому клик по ресурсу и
фильтр `/management/resource/{name}` работают без настройки. Источники
задаются у контейнера дашборда:

```yaml
Management__Sources__0__Name: app1
Management__Sources__0__Url: http://app1:8080
Management__Sources__0__PublicUrl: http://localhost:8081
Management__Sources__1__Name: app2
Management__Sources__1__Url: http://app2:8080
Management__Sources__1__PublicUrl: http://localhost:8082
```

## Файловый браузер

Вкладки **«Логи»** и **«Share»** опциональны: если путь не задан, вкладка не появляется.

```yaml
environment:
  Dashboard__Files__Logs__Path: /workspace/logs
  Dashboard__Files__Share__Path: /host-home
```

Корни:

- `/workspace/logs` — `logs/` текущего проекта;
- `/host-home` — домашняя директория хоста, примонтирована read-only.

```yaml
volumes:
  - ./:/workspace:ro
  - ${HOME}:/host-home:ro
```

Возможности:

- маски: `*.log`, `*.txt;*.json`, `*`;
- папки выше файлов;
- колонки: иконка, `Name`, `Size`, `Type`, `Date Modified`, `view`;
- вход в папку, `Вверх`, `В корень`, клавиша **Home**;
- скачивание: `/files/download?tab=logs&path=app1.log`;
- просмотр UTF-8 до 5 МБ;
- подсветка JSON и YAML через highlight.js;
- preview на всю область с кнопками back/download;
- защита от `../` path traversal.

Скриншоты: `docs/file-browser.png`, `docs/file-preview.png`.

## Метрики и логи

Приложения отправляют OTLP на:

```text
http://aspire-dashboard:18889
```

Метрики app2:

- `app2.counter.value`;
- `app2.counter.increments`.

Логи:

- stdout контейнера;
- OTLP → **Structured logs** в dashboard;
- уровни `Information/Warning/Error/Debug/Trace` отображаются SVG-иконками.

## Конфигурация

`.env`:

```dotenv
COMPOSE_PROJECT_NAME=otel-demo
BIND_ADDR=127.0.0.1

DASHBOARD_PORT=18888
OTLP_PORT=4317
APP1_PORT=8081
APP2_PORT=8082

DASHBOARD_APP_NAME=РА-Монитор
DASHBOARD_SUPPRESS_UNSECURED=true
DASHBOARD_LOGS_PATH=/workspace/logs
DASHBOARD_SHARE_PATH=/host-home

ASPIRE_DASHBOARD_IMAGE=ra-monitor-dashboard:17.0.0
```

Ключевые переменные `app2`:

| Переменная | Назначение |
|---|---|
| `OTEL_SERVICE_NAME` | имя сервиса в OTel (и имя источника панелей) app2 |

Ключевые переменные `aspire-dashboard` (источники панелей):

| Переменная | Назначение |
|---|---|
| `Management__Sources__0__Name` | имя первого источника (`app1`) |
| `Management__Sources__0__Url` | внутренний адрес app1 |
| `Management__Sources__0__PublicUrl` | внешний адрес app1 для UI |
| `Management__Sources__1__*` | вторым источником идёт `app2` |
| `Management__TimeoutSeconds` | таймаут запроса к источнику (10) |

## Сборка

Приложения:

```bash
dotnet build OtelDemo.slnx -c Release
```

Кастомный dashboard:

```bash
cd dashboard/aspire
dotnet build src/Aspire.Dashboard/Aspire.Dashboard.csproj -c Release -p:PublishAot=false
```

Приложения на .NET 10. Для сборки dashboard нужен .NET 11 SDK, потому что исходники Aspire 17 target `net11.0`.

Всё через Docker:

```bash
docker compose up -d --build
```

## Безопасность

Шаг 1 — демо:

- порты привязаны к `127.0.0.1`;
- dashboard без логина (`ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true`);
- OTLP/app API без ключей;
- файловый браузер читает проект и домашнюю директорию **read-only**;
- `/files/download` и `/dashboard/*` не защищены отдельной авторизацией.

Для production:

1. включите логин dashboard и API-ключи;
2. закройте `POST /api/messages`, `/dashboard/*`, `/files/download`;
3. не монтируйте домашнюю директорию;
4. используйте HTTPS и reverse proxy с авторизацией.

## Troubleshooting

- **Dashboard не открывается** — `docker compose ps`, `docker compose logs aspire-dashboard`.
- **Нет панелей** — `curl http://localhost:8082/dashboard/panels`; источники задаются `Management__Sources__*` у контейнера дашборда.
- **Нет вкладок Логи/Share** — не задан `Dashboard__Files__Logs__Path` / `Share__Path`.
- **Нет JSON/YAML подсветки** — preview доступен только для UTF-8 файлов до 5 МБ.
- **Порт занят** — измените `DASHBOARD_PORT` / `APP*_PORT` в `.env`.
