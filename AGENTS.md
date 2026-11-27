# AGENTS.md — инструкции для агентов

Этот файл описывает, как безопасно запускать, менять и проверять проект
**РА-Монитор**. Документация для людей — в [README.md](README.md), DI и
настройки — в [docs/di.md](docs/di.md), экраны — в
[docs/presentation.md](docs/presentation.md).

## Коротко

РА-Монитор — демо на Docker Compose:

- кастомный **Aspire Dashboard** (`.NET 11`, собран из исходников);
- **app1/app2** (`.NET 10`) с панелями, поисковыми фильтрами и OTLP;
- **otlp-js-test** (`Node.js`) с собственной метрикой и панелью;
- файловый браузер, уведомления, viewers и автообновление панелей.

## Пример запуска

Канонический способ — Docker Compose:

```bash
cd /home/du/Documents/sources/test/otel
docker compose up -d --build
docker compose ps
```

Открыть:

```text
Dashboard:    http://localhost:18888
Управление:   http://localhost:18888/management
Поиск:        http://localhost:18888/search
Файлы:        http://localhost:18888/files/logs
JS app:       http://localhost:8083
```

Проверка после старта:

```bash
curl -fsS http://127.0.0.1:18888/api/registry | python3 -m json.tool
curl -fsS http://127.0.0.1:8082/dashboard/panels
curl -fsS http://127.0.0.1:8083/api/state
curl -fsS http://127.0.0.1:8083/dashboard/panels/JsCounterPanel
```

Ожидаемо: в registry есть `app1`, `app2`, `otlp-js-test`; у app2 и JS-панели
в дескрипторе `refreshIntervalMs: 1000`; у JS-панели label
**«все будет Js»**.

Остановить:

```bash
docker compose down
```

## Требования

| Инструмент | Зачем |
|---|---|
| .NET 10 SDK | `src/App1.Api`, `src/App2.Counter`, `RaMonitor.Panels` (`net10.0`) |
| .NET 11 SDK | `dashboard/aspire`, `dashboard/host`, `RaMonitor.Panels` (`net11.0`) |
| Node.js 22+ | `src/OtlpJsTest` (без npm-зависимостей) |
| Docker + Compose | канонический полный запуск |

Если .NET 11 установлен рядом с .NET 10:

```bash
export DOTNET_ROOT="$HOME/.dotnet11"
export PATH="$DOTNET_ROOT:$PATH"
dotnet --version
```

> В этом окружении .NET 11 SDK лежит в `/home/du/.dotnet11`.

## Структура

```text
src/App1.Api/          .NET 10: сообщения, логи, панель app1
src/App2.Counter/      .NET 10: счётчик, панели, migration, поиск
src/OtlpJsTest/        Node.js: server.js + Dockerfile, метрика и панель
dashboard/aspire/      исходники Aspire Dashboard (net11.0)
dashboard/host/        web-хост dashboard, Program.cs, appsettings.json
dashboard/panels/      RaMonitor.Panels — SDK панелей (net10.0;net11.0)
docs/                  di.md, presentation.md, скриншоты
compose.yaml           полный стек
```

## Запуск по частям

### Только dashboard (локально, .NET 11)

```bash
export DOTNET_ROOT="$HOME/.dotnet11"
export PATH="$DOTNET_ROOT:$PATH"

cd dashboard/host
ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL=http://0.0.0.0:18890 \
dotnet run -c Release -p:PublishAot=false
```

UI: `http://localhost:18888`.

### app1/app2 (локально, .NET 10)

Запускать в отдельных терминалах. Для локального host используйте
`localhost` вместо Docker DNS:

```bash
OTEL_SERVICE_NAME=app1 \
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:18889 \
Dashboard__RegistryUrl=http://localhost:18888/api/registry \
Service__Name=app1 \
Service__InternalUrl=http://localhost:8081 \
Service__PublicUrl=http://localhost:8081 \
ASPNETCORE_URLS=http://localhost:8081 \
dotnet run --project src/App1.Api/App1.Api.csproj -c Release
```

```bash
OTEL_SERVICE_NAME=app2 \
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:18889 \
Dashboard__RegistryUrl=http://localhost:18888/api/registry \
Service__Name=app2 \
Service__InternalUrl=http://localhost:8082 \
Service__PublicUrl=http://localhost:8082 \
ASPNETCORE_URLS=http://localhost:8082 \
dotnet run --project src/App2.Counter/App2.Counter.csproj -c Release
```

> Полный локальный запуск требует, чтобы dashboard видел `InternalUrl`.
> Проще использовать Docker Compose.

### JS app (локально, Node.js)

```bash
SERVICE_NAME=otlp-js-test \
DASHBOARD_REGISTRY_URL=http://localhost:18888/api/registry \
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:18890 \
SERVICE_INTERNAL_URL=http://localhost:8083 \
SERVICE_PUBLIC_URL=http://localhost:8083 \
PORT=8083 \
node src/OtlpJsTest/server.js
```

Если dashboard запущен в Docker, используйте опубликованные порты
`http://localhost:4317` (gRPC) и `http://localhost:4318` (HTTP) вместо
`18889`/`18890`.

JS app отправляет `js.counter.value` через OTLP/HTTP JSON:

```text
POST http://localhost:18890/v1/metrics   # локальный dashboard
POST http://localhost:4318/v1/metrics    # dashboard в Compose
Content-Type: application/json
```

## Сборка и проверки

```bash
# Приложения + SDK панелей (net10)
dotnet build OtelDemo.slnx -c Release

# Dashboard и хост (net11)
export DOTNET_ROOT="$HOME/.dotnet11"
export PATH="$DOTNET_ROOT:$PATH"
cd dashboard/host
dotnet build RaMonitor.Host.csproj -c Release -p:PublishAot=false

# JS
node --check src/OtlpJsTest/server.js

# Compose
docker compose config
```

E2E-проверка UI — через `chrome-devtools` MCP (настроен в
`opencode.json`). Playwright в репозитории не используется.

Проверить панели и действия:

```bash
# Все панели приложения
curl -fsS http://127.0.0.1:8082/dashboard/panels | python3 -m json.tool

# Одна панель (auto-refresh)
curl -fsS http://127.0.0.1:8082/dashboard/panels/CounterPanel | python3 -m json.tool

# Действие «Миграция» (в демо ждёт 4 секунды)
curl -fsS -X POST \
  http://127.0.0.1:8082/dashboard/panels/MigrationPanel/actions/Report \
  -H 'Content-Type: application/json' -d '{}' | python3 -m json.tool
```

## Контракты панелей

```text
GET  /dashboard/panels
GET  /dashboard/panels/{panelId}
POST /dashboard/panels/{panelId}/actions/{actionName}
```

Ключевые поля дескриптора:

- `kind` — `panel` или `search`;
- `elements` — `label`, `note`, `info`, `warn`;
- `refreshIntervalMs` — опциональный таймер автообновления;
- `actions` — кнопки, выполняются через `IDashboardPanel.ExecuteAsync`.

Автообновление включается только явно:

```csharp
.Panel<CounterPanel>("Счётчик app2", order: 10)
    .AutoRefresh(TimeSpan.FromSeconds(1))
```

Дашборд перечитывает панель через `GET /dashboard/panels/{id}`. Обновляются
read-only поля и поля, которые пользователь ещё не редактировал.

## Порты

| Порт | Что |
|---:|---|
| 18888 | Dashboard UI |
| 18889 → 4317 | OTLP/gRPC |
| 18890 → 4318 | OTLP/HTTP |
| 8081 | app1 |
| 8082 | app2 |
| 8083 | otlp-js-test |

Все порты привязаны к `127.0.0.1` через `BIND_ADDR`.

## Правила и подводные камни

- Не добавлять `iframe`, `nginx` и Aspire AppHost — проект строится на
  библиотеке dashboard + web-хосте + Compose.
- `dashboard/aspire` **не должен** ссылаться на `RaMonitor.Panels`:
  конфликт версий OpenTelemetry. Связка идёт через `dashboard/host`.
- `OtelDemo.slnx` — решение для .NET 10 (apps + panels net10). Не добавлять
  в него `Aspire.Dashboard`: решение перестанет собираться на .NET 10.
- Для `dashboard/aspire` и `dashboard/host` нужен **.NET 11 SDK**.
- После изменения кода пересобирать нужный контейнер:
  ```bash
  docker compose build app1 app2 aspire-dashboard otlp-js-test
  docker compose up -d
  ```
- После рестарта dashboard registry пуст: приложения регистрируются заново
  в течение ~10 секунд. JS app повторяет `register`, .NET-приложения —
  `heartbeat`.
- Если JS-метрики нет, проверить `ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL`,
  порт `18890`, `Content-Type: application/json` и путь `/v1/metrics`.
- Документация и UI — на русском. При изменении поведения обновлять
  `README.md`, `dashboard/README.md`, `docs/di.md` и при необходимости
  `docs/presentation.md`.
- Скриншоты презентации лежат в `docs/presentation/`; ссылки в Markdown
  должны быть относительными, чтобы работать на GitHub.

## Полезные команды

```bash
docker compose logs -f aspire-dashboard
docker compose logs -f app2
docker compose logs -f otlp-js-test
docker compose restart aspire-dashboard
docker compose down
```

## Что проверять перед коммитом

1. `dotnet build OtelDemo.slnx -c Release` проходит на .NET 10.
2. `dotnet build dashboard/host/RaMonitor.Host.csproj -c Release -p:PublishAot=false`
   проходит на .NET 11.
3. `node --check src/OtlpJsTest/server.js` без ошибок.
4. `docker compose config` валиден.
5. UI проверен на `http://localhost:18888`: управление, поиск, файлы,
   автообновление, спиннеры, уведомления.
6. В консоли браузера нет ошибок.
