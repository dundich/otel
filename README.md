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

app1/app2 ── POST /api/registry/register ──→ dashboard
app1/app2 ── heartbeat каждые 10 сек ───────→ dashboard

dashboard /management ──→ app1: /dashboard/panels
                     └──→ app2: /dashboard/panels
```

Приложения сами регистрируют свой internal/public URL в dashboard. Дашборд
запрашивает панели **на сервере** по зарегистрированным internal URL, поэтому
proxy и CORS не нужны.

## Панели приложений

Панель — класс, реализующий `IDashboardPanel`. Метаданные (заголовок, поля,
кнопки) задаются **fluent-билдером** при регистрации; атрибуты — лишь сахар
для `ScanAssembly`.

```csharp
public sealed class MyPanel(ILogger<MyPanel> logger) : IDashboardPanel
{
    public string Name { get; set; } = "";
    public DateOnly BirthDate { get; set; }
    public int Count { get; set; }
    public bool Enabled { get; set; }

    // при появлении панели: каждый GET /dashboard/panels
    public Task RenderAsync(CancellationToken ct = default)
    {
        Name = "Ваня";
        return Task.CompletedTask;
    }

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

Регистрация — один вызов на билдере (DI + endpoints):

```csharp
builder.AddDashboardPanels(panels => panels
    .Panel<MyPanel>("Моя панель", order: 10)
        .Input(p => p.Name, "Имя", DashboardInputType.Text, sortOrder: 1)
        .Input(p => p.BirthDate, "Дата рождения", DashboardInputType.Date, sortOrder: 2)
        .Input(p => p.Count, "Количество", DashboardInputType.Int, sortOrder: 3)
        .Input(p => p.Enabled, "Активно", DashboardInputType.Bool, sortOrder: 4)
        .Action(p => p.Apply(), "Применить", sortOrder: 1));
```

`DashboardInputType.ReadOnly` показывает значение только для чтения: свойству
достаточно геттера, и при нажатии кнопок такое поле не отправляется обратно
в приложение.

Атрибуты — сахар: если классы размечены `[DashboardPanel]`/`[DashboardInput]`/
`[DashboardAction]`, достаточно `builder.AddDashboardPanels<Program>()`
(скан сборки) или `.ScanAssembly(typeof(Program).Assembly)` внутри билдера.

В этом репозитории показаны оба варианта:

- **app1** — «сахар»: панель размечена атрибутами, регистрация
  `builder.AddDashboardPanels<Program>()`;
- **app2** — fluent: `builder.AddDashboardPanels(panels => panels.Panel<CounterPanel>(...))`.

В рантайме запроса рефлексии нет: доступы к полям компилируются в делегаты,
а действия вызываются через `IDashboardPanel.ExecuteAsync`:

- `RenderAsync()` — вызывается **при появлении панели**: каждый
  `GET /dashboard/panels` (открытие/обновление страницы «Управление»),
  пересчитывает значения полей перед сериализацией в дашборд;
- `ExecuteAsync(actionName)` — вызывается **по нажатию кнопки действия**:
  клик → SignalR circuit → `POST .../actions/{actionName}` → сначала
  `BindValues` записывает значения формы в свойства, затем диспетчер
  `switch (actionName)` вызывает нужный метод напрямую.

Панель — singleton с обычным constructor injection (`ILogger`, `CounterService`, ...).
Контракт — интерфейс `IDashboardPanel`: `RenderAsync` вызывается при появлении
панели (каждый `GET /dashboard/panels`), `ExecuteAsync` — по нажатию кнопки
действия. Рефлексии в рантайме нет: атрибуты читаются один раз при регистрации,
доступы к свойствам компилируются в делегаты.

### Управление

На странице **«Управление»**:

- фильтр **«Приложение»**: все / зарегистрированные сервисы (`app1`, `app2`);
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
`app1:8080/dashboard/panels` и видна в Management, потому что app1 сам
регистрирует `http://app1:8080` в dashboard при старте.

- **«Счётчик app1»** (`CounterPanel` + свой `CounterService`) — независимый
  счётчик этого приложения: `Step`/`Enabled`, «Применить», «Сбросить»
  с confirm. Тикает раз в секунду, как и счётчик app2. Метаданные заданы
  **атрибутами** (`[DashboardPanel]`/`[DashboardInput]`/`[DashboardAction]`),
  регистрация — `builder.AddDashboardPanels<Program>()`.

Журнал сообщений (`MessageStore`, `POST/GET /api/messages`) остаётся API
приложения, отдельной панели у него больше нет.

### Панели app2

app2 — обычное приложение со своими панелями, без роли агрегатора. Панель
**«Счётчик app2»** (`CounterPanel`) объявлена **fluent-билдером** и управляет
живым `CounterService`:

- `RenderAsync` — при появлении панели читает `Step`/`Enabled` из сервиса;
- **«Применить»** — `SetStep`/`SetEnabled`;
- **«Сбросить»** — `counter.Reset()`, с `RequireConfirm` (confirm в UI).

Агрегацию панелей делает **сам дашборд** (домен
`dashboard/aspire/src/Aspire.Dashboard/Management/`): он читает registry
и ходит в каждое зарегистрированное приложение напрямую. Имена источников
совпадают с OTel-именами (`OTEL_SERVICE_NAME`), поэтому клик по ресурсу и
фильтр `/management/resource/{name}` работают без настройки.

### Service discovery

Каждое приложение регистрирует себя в dashboard:

```yaml
environment:
  Dashboard__RegistryUrl: http://aspire-dashboard:18888/api/registry
  Service__Name: app1
  Service__InternalUrl: http://app1:8080
  Service__PublicUrl: http://localhost:8081
```

- `InternalUrl` — адрес, по которому dashboard вызывает приложение;
- `PublicUrl` — адрес для отображения в UI;
- при старте приложение делает `POST /api/registry/register`;
- каждые 10 секунд — `POST /api/registry/heartbeat`;
- при остановке — `DELETE /api/registry/{id}`;
- записи без heartbeat дольше 45 секунд удаляются.

Registry хранится в памяти dashboard. Для нескольких реплик dashboard нужен
общий store (Redis/Postgres).

В Kubernetes используйте Service DNS:

```text
Service__InternalUrl: http://app1.default.svc.cluster.local:8080
Dashboard__RegistryUrl: http://dashboard.default.svc.cluster.local:18888/api/registry
```

`Management:Sources` остаётся статическим fallback-ом, если registry пуст.

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

Ключевые переменные приложений (`app1`/`app2`):

| Переменная | Назначение |
|---|---|
| `OTEL_SERVICE_NAME` | имя сервиса в OTel и имя источника панелей |
| `Dashboard__RegistryUrl` | registry dashboard (`http://aspire-dashboard:18888/api/registry`) |
| `Service__Name` | имя регистрации (`app1`/`app2`) |
| `Service__InternalUrl` | адрес для callback от dashboard |
| `Service__PublicUrl` | адрес для отображения в UI |

Ключевые переменные `aspire-dashboard`:

| Переменная | Назначение |
|---|---|
| `Management__TimeoutSeconds` | таймаут запроса к источнику (10) |
| `Management__Sources__*` | статический fallback, если registry пуст |

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
- **Нет панелей** — `curl http://localhost:18888/api/registry`; проверьте `Dashboard__RegistryUrl` и heartbeat приложения. `Management__Sources__*` — только fallback.
- **Нет вкладок Логи/Share** — не задан `Dashboard__Files__Logs__Path` / `Share__Path`.
- **Нет JSON/YAML подсветки** — preview доступен только для UTF-8 файлов до 5 МБ.
- **Порт занят** — измените `DASHBOARD_PORT` / `APP*_PORT` в `.env`.
