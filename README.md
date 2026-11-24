# РА-Монитор

Минимальный пример на **.NET 10**: кастомный **Aspire Dashboard**, собранный из исходников, плюс два приложения с панелями управления и файловым браузером.

Без Aspire AppHost, без nginx и без iframe. Всё поднимается через `docker compose`.

## Что внутри

```text
src/
  Dashboard.Client/     # OTel + SDK панелей: атрибуты, DI, /dashboard/panels
  App1.Api/             # Web API: принимает сообщения, пишет лог, панель app1
  App2.Counter/         # счётчик +1/сек, OTLP, агрегация панелей app1
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

app2 ── /api/panels ──→ dashboard /management
app1 ←── POST /api/messages ── app2
```

Dashboard запрашивает панели у app2 **на сервере** (`Management__ApiBaseUrl=http://app2:8080/api`), поэтому proxy и CORS не нужны.

## Панели приложений

`Dashboard.Client` даёт атрибуты:

```csharp
[DashboardPanel("Панель app1", order: 10)]
public sealed class App1DemoPanel(ILogger<App1DemoPanel> logger)
{
    [DashboardInput("Имя", sortOrder: 1)]
    public string Name { get; set; } = "";

    [DashboardInput("Дата рождения", DashboardInputType.Date, sortOrder: 2)]
    public DateOnly BirthDate { get; set; }

    [DashboardInput("Количество", DashboardInputType.Int, sortOrder: 3)]
    public int Count { get; set; }

    [DashboardInput("Активно", DashboardInputType.Bool, sortOrder: 4)]
    public bool Enabled { get; set; }

    public Task Render() { Name = "Ваня"; return Task.CompletedTask; }

    [DashboardAction("Применить")]
    public Task<string> Apply() => Task.FromResult($"Принято: {Name}, {Count}");
}
```

Подключение через DI в приложении:

```csharp
builder.Services.AddDashboardPanel<App1DemoPanel>();
app.MapDashboardPanels();
```

Панель — singleton с обычным constructor injection (`ILogger`, `CounterService`, ...).

### Управление

На странице **«Управление»**:

- фильтр **«Приложение»**: все / app1;
- клик по `app1` в левом resource pane фильтрует панели;
- поля `text`, `date`, `int`, `bool`;
- кнопка **«Применить»**;
- версия приложения в заголовке панели: `Панель app1 v1.0.0`;
- `Public` / `Internal` адреса приложения;
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

SDK записывает значения в свойства, вызывает action и возвращает полный снимок панели.

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
- `app2.counter.increments`;
- `app2.messages.sent`;
- `app2.messages.sent.value`.

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
| `App1__BaseUrl` | адрес app1 (`http://app1:8080`) |
| `Dashboard__Sources__0__Name` | имя источника панелей (`app1`) |
| `Dashboard__Sources__0__Url` | внутренний адрес app1 |
| `Dashboard__Sources__0__PublicUrl` | внешний адрес для отображения |
| `Management__ApiBaseUrl` | API app2 для Management dashboard |

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
- **Нет панелей** — `curl http://localhost:8082/api/panels`; проверьте `Dashboard:Sources`.
- **Нет вкладок Логи/Share** — не задан `Dashboard__Files__Logs__Path` / `Share__Path`.
- **Нет JSON/YAML подсветки** — preview доступен только для UTF-8 файлов до 5 МБ.
- **Порт занят** — измените `DASHBOARD_PORT` / `APP*_PORT` в `.env`.
