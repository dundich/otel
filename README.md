# РА-Монитор

Минимальный пример: кастомный **Aspire Dashboard** (**.NET 11**), собранный из
исходников, плюс два приложения на **.NET 10** с панелями управления,
поисковыми фильтрами и файловым браузером.

Дашборд разделён на **библиотеку-компонент** (`dashboard/aspire`) и
**web-хост** (`dashboard/host`). Хост владеет всеми настройками и подключает
дашборд через DI. Это позволяет в будущем встроить dashboard в другой UI-хост
или смонтировать его под своим base URL.

Без Aspire AppHost, без nginx и без iframe. Всё поднимается через `docker compose`.

> 📽 **Презентация экранов:** [docs/presentation.md](docs/presentation.md) —
> 12 скриншотов с описанием: Управление, Поиск, Файлы, уведомления и телеметрия.

## Содержание

- [Актуальность](#актуальность)
- [Презентация](#презентация)
- [Что внутри](#что-внутри)
- [Быстрый старт](#быстрый-старт)
- [Dashboard как компонент](#dashboard-как-компонент)
  - [Архитектура](#архитектура)
  - [Регистрация в DI](#регистрация-в-di)
  - [Домены настроек](#домены-настроек)
  - [FromConfiguration](#fromconfiguration)
  - [Настройки хоста](#настройки-хоста)
  - [Свой base URL у каждого хоста](#свой-base-url-у-каждого-хоста)
  - [Как встроить в другой хост](#как-встроить-в-другой-хост)
  - [Технические детали](#технические-детали)
  - [Версия хоста](#версия-хоста)
  - [Проверка](#проверка)
- [Как это работает](#как-это-работает)
- [Панели приложений](#панели-приложений)
  - [Управление](#управление)
  - [API панелей](#api-панелей)
  - [Результат действия](#результат-действия)
  - [Элементы панели](#элементы-панели)
  - [Поисковые панели](#поисковые-панели)
  - [Viewers](#viewers)
  - [Уведомления](#уведомления)
  - [Панели app1](#панели-app1)
  - [Панели app2](#панели-app2)
  - [Service discovery](#service-discovery)
- [Файловый браузер](#файловый-браузер)
- [Метрики и логи](#метрики-и-логи)
- [Конфигурация](#конфигурация)
- [Сборка](#сборка)
- [Безопасность](#безопасность)
- [Troubleshooting](#troubleshooting)
- [Лицензия](#лицензия)

## Актуальность

**Актуально на 2026-11-26.** Всё ниже проверено в Docker-сборке.

| Компонент | Актуальное состояние |
|---|---|
| Dashboard | Библиотека `Aspire.Dashboard` + хост `dashboard/host`, .NET 11 |
| Приложения | `app1`, `app2` на .NET 10, OTLP/gRPC, self-registration |
| SDK панелей | `RaMonitor.Panels` (`net10.0;net11.0`), обычные и поисковые панели |
| Вкладки | Structured logs, Traces, Metrics, **Управление**, **Поиск**, **Файлы** |
| Панели | элементы Label/Note/Info/Warn, типизированные ответы, viewers |
| Уведомления | список событий + открытие ответа по клику |
| Презентация | 12 экранов со скриншотами в `docs/presentation/` |

## Презентация

Пошаговый обзор интерфейса:

- [docs/presentation.md](docs/presentation.md) — вся презентация;
- [Управление: миграции](docs/presentation/management-migration.png);
- [Поиск: накладная № 222](docs/presentation/search-invoice.png);
- [Файлы: список Share](docs/presentation/files-share-list.png);
- [Центр уведомлений](docs/presentation/notifications.png).

Картинки лежат рядом с документом в `docs/presentation/`, поэтому видны и на
GitHub при просмотре Markdown.

## Что внутри

```text
src/
  App1.Api/             # Web API: принимает сообщения, пишет лог, панель app1
  App2.Counter/         # счётчик +1/сек, OTLP, свои панели
dashboard/
  aspire/               # vendored исходники Aspire 17.0.0 + AddDashboard()
  host/                 # web-хост: Program.cs, appsettings.json, все настройки
  panels/               # «Панельки»: SDK панелей приложений (RaMonitor.Panels)
  Dockerfile            # сборка host
docs/
  di.md                 # DI, домены настроек, lifetime
  presentation.md       # презентация экранов
  presentation/         # скриншоты для презентации
logs/                   # корень одного из узлов вкладки «Файлы»
compose.yaml
```

Контейнеры:

- `aspire-dashboard` — web-хост `dashboard/host`, который подключает dashboard через DI;
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
Structured logs   Traces   Metrics   Управление   Поиск   Файлы
```

Презентация экранов с картинками: [docs/presentation.md](docs/presentation.md).

Полезные адреса:

| Адрес | Что это |
|---|---|
| http://localhost:18888 | Aspire Dashboard |
| http://localhost:18888/structuredlogs | логи |
| http://localhost:18888/traces | трейсы |
| http://localhost:18888/metrics | метрики |
| http://localhost:18888/management | панели приложений |
| http://localhost:18888/search | поисковые фильтры |
| http://localhost:18888/files/logs | файловый браузер логов |
| http://localhost:18888/files/share | файловый браузер домашней директории |
| http://localhost:8081 | app1 API |
| http://localhost:8082 | app2 API |
| http://localhost:4317 | OTLP/gRPC |

Остановить:

```bash
docker compose down
```

## Dashboard как компонент

> Подробный разбор DI-регистрации, доменов, lifetime и встраивания в другой
> хост — [`docs/di.md`](docs/di.md). Рабочий пример —
> `dashboard/host/Program.cs`.

### Архитектура

Дашборд — это не отдельное приложение, а **библиотека** `Aspire.Dashboard`
с fluent-регистрацией в DI. Web-хост (`dashboard/host`) — обычное ASP.NET Core
приложение, которое:

- создаёт `WebApplicationBuilder`;
- передаёт дашборду свои настройки через доменные билдеры;
- вызывает `UseDashboard()` для middleware и endpoints;
- владеет `appsettings.json`, статикой, портами и брендингом.

```text
dashboard/host/RaMonitor.Host        web-хост, точка входа
        │  ProjectReference
        ▼
dashboard/aspire/Aspire.Dashboard    библиотека: UI, OTLP, API, registry, файлы
```

| Проект | Ответственность |
|---|---|
| `dashboard/host` | `Program.cs`, конфигурация, `wwwroot`, логотип, Docker |
| `dashboard/aspire` | Blazor-компоненты, OTLP-приём, telemetry API, registry, Management, файловый браузер |
| `dashboard/panels` («Панельки», `RaMonitor.Panels`) | SDK панелей для приложений (`AddDashboardPanels`, `IDashboardPanel`) |
| `src/App1.Api`, `src/App2.Counter` | демо-приложения, отправляют OTLP и регистрируют себя |

Границы важны: библиотека **не читает** `appsettings.json` хоста сама и не
создаёт `WebApplication`. Всё, что зависит от окружения, приходит из хоста.

### Регистрация в DI

Минимальный хост:

```csharp
using Aspire.Dashboard;

var builder = WebApplication.CreateBuilder(args);

builder.AddDashboard(dashboard => dashboard
    .Branding(branding => branding
        .Title("РА-Монитор")
        .LogoUrl("/img/logo-ra.svg"))
    .Hosting(hosting => hosting
        .BasePath("/")
        .PublicUrl("http://localhost:18888")
        .EndpointUrls("http://0.0.0.0:18888"))
    .Otlp(otlp => otlp
        .GrpcEndpoint("http://0.0.0.0:18889")
        .SuppressUnsecuredMessage(true))
    .Files(files => files
        .Title("Файлы")
        .Root("logs", root => root
            .Title("Логи")
            .Path("/workspace/logs")
            .Url("/files/logs"))
        .Root("share", root => root
            .Title("Share")
            .Path("/host-home")
            .Url("/files/share")))
    .Management(management => management
        .Timeout(10))
    .Security(security => security
        .AllowAnonymous(true)));

var app = builder.Build();

app.UseDashboard();

app.Run();
```

`AddDashboard()`:

- создаёт `DashboardBuilder` и применяет домены;
- переносит значения в `IConfiguration` (in-memory provider добавляется последним,
  поэтому настройки хоста выигрывают у env);
- регистрирует `DashboardComponentOptions` и `DashboardWebApplication` как
  singleton в DI;
- добавляет все сервисы дашборда: Blazor Server, Fluent UI, OTLP, telemetry,
  registry, Management, файловый браузер, авторизацию;
- настраивает Kestrel endpoints из `Hosting.EndpointUrls` и OTLP endpoint.

`UseDashboard()`:

- монтирует base path (`UsePathBase`);
- добавляет `UseRouting` **после** `UsePathBase`, иначе `_blazor` останется
  в корне хоста;
- настраивает localization, CORS, forwarded headers, auth, antiforgery;
- подключает статику и `MapStaticAssets()` (включая `_framework/blazor.web.js`);
- маппит Blazor-компоненты, OTLP/gRPC, OTLP/HTTP, telemetry API, registry,
  health checks, файловый браузер.

Порядок middleware намеренно повторяет оригинальный Aspire Dashboard: auth
идёт после forwarded headers и CORS, antiforgery — после authorization,
а OTLP endpoints не пересекаются с UI.

### Домены настроек

Каждый домен — отдельный билдер. Методы можно вызывать в любом порядке;
цепочка возвращает `DashboardBuilder`.

| Домен | Билдер | Методы |
|---|---|---|
| Брендинг | `Branding` | `Title(string)`, `LogoUrl(string)` |
| Хостинг | `Hosting` | `BasePath(string)`, `PublicUrl(string)`, `EndpointUrls(string)` |
| Файлы | `Files` | `Title(string)`, `Root(id, root => ...)`, `Roots(roots => ...)` |
| OTLP | `Otlp` | `GrpcEndpoint(string)`, `HttpEndpoint(string)`, `Unsecured(bool)`, `SuppressUnsecuredMessage(bool)` |
| Управление | `Management` | `Timeout(int)`, `Source(name, url, publicUrl?)` |
| Безопасность | `Security` | `AllowAnonymous(bool)` |
| Панели хоста | `Panels` (host extension) | `Panel<T>(...)`, `ScanAssembly<T>()` |

#### Branding

```csharp
.Branding(branding => branding
    .Title("РА-Монитор")
    .LogoUrl("/img/logo-ra.svg"))
```

- `Title` попадает в `Dashboard:ApplicationName` и
  `ASPIRE_DASHBOARD_APPLICATION_NAME`, поэтому используется в шапке,
  `<title>` и login-странице.
- `LogoUrl` подставляется в компонент `AspireLogo`.
- Если `LogoUrl` начинается с `/`, base path добавляется автоматически:
  `/monitor` + `/img/logo-ra.svg` → `/monitor/img/logo-ra.svg`.
- Абсолютный URL (`https://cdn/logo.svg`) не переписывается.
- Логотип — обычный файл хоста в `wwwroot`; в демо это
  `dashboard/host/wwwroot/img/logo-ra.svg`.

#### Hosting

```csharp
.Hosting(hosting => hosting
    .BasePath("/monitor")
    .PublicUrl("https://example.com/monitor")
    .EndpointUrls("http://0.0.0.0:18888"))
```

- `BasePath` — путь монтирования UI, API и Blazor-цепочки.
- `PublicUrl` — адрес для ссылок и отображения (может отличаться от listen URL).
- `EndpointUrls` — Kestrel listen URL; в контейнере обычно
  `http://0.0.0.0:18888`.

`BasePath` поддерживает `_blazor`, статику, CSS, логотип и ссылки навигации.
Клиентский `base href` формируется автоматически: `/monitor/`.

#### Files

Одна вкладка **«Файлы»** с выпадающим списком корней. Билдер вложенный:
`Files.Title` — название вкладки, `Root` — дочерний элемент combo со своими
`Title`, `Path` и `Url`.

```csharp
.Files(files => files
    .Title("Файлы")
    .Root("logs", root => root
        .Title("Логи")
        .Path("/workspace/logs")
        .Url("/files/logs"))
    .Root("share", root => root
        .Title("Share")
        .Path("/host-home")
        .Url("/files/share")))
```

- `Title` на `Files` — заголовок вкладки в навигации.
- `Root(id, root => ...)` — дочерний узел combo.
- `.Title(...)` у корня — подпись в combo.
- `.Path(...)` — путь на диске.
- `.Url(...)` — адрес страницы (по умолчанию `/files/{id}`).
- `null`-путь удаляет узел.

Все корни из конфигурации можно добавить циклом через `Roots`:

```csharp
.Files(files => files
    .Title(builder.Configuration["Dashboard:Files:Title"])
    .Roots(roots =>
    {
        foreach (var root in builder.Configuration
            .GetSection("Dashboard:Files:Roots").GetChildren())
        {
            roots.Root(root.Key, fileRoot => fileRoot
                .Title(root["Title"])
                .Path(root["Path"])
                .Url(root["Url"]));
        }
    }))
```

Ключи:

```text
Dashboard:Files:Title
Dashboard:Files:Roots:{id}:Path
Dashboard:Files:Roots:{id}:Title
Dashboard:Files:Roots:{id}:Url
```

В UI вкладка одна; выбор корня в combo меняет URL страницы:
`/files/logs` ↔ `/files/share`.

#### Otlp

```csharp
.Otlp(otlp => otlp
    .GrpcEndpoint("http://0.0.0.0:18889")
    .HttpEndpoint("http://0.0.0.0:18890")
    .SuppressUnsecuredMessage(true))
```

- gRPC — основной канал для приложений;
- HTTP endpoint опционален;
- `Unsecured()` выставляет OTLP auth mode в `Unsecured`;
- `SuppressUnsecuredMessage()` убирает предупреждение в UI.

#### Management

```csharp
.Management(management => management
    .Timeout(10)
    .Source("app1", "http://app1:8080", "http://localhost:8081"))
```

`Source` — статический fallback. Основной путь — registry: приложения сами
публикуют свой internal URL. Если registry пуст, дашборд использует
`Management:Sources`.

#### Security

```csharp
.Security(security => security.AllowAnonymous(true))
```

Шаг-1 демо: без логина и API-ключей. В production вызывайте
`AllowAnonymous(false)` и настраивайте auth/keys через стандартную
конфигурацию Aspire.

#### Panels (панели хоста)

Хостовое расширение `Panels` переиспользует существующий SDK
`RaMonitor.Panels` и его `AddDashboardPanels`. Вызов остаётся внутри цепочки
`AddDashboard`:

```csharp
builder.AddDashboard(dashboard => dashboard
    ...
    .Panels(panels => panels.ScanAssembly<HostInfoPanel>()));
```

Или явно:

```csharp
.Panels(panels => panels
    .Panel<HostInfoPanel>("Хост", order: 100)
        .Input(p => p.Version, "Версия", DashboardInputType.ReadOnly))
```

Что происходит:

- вызывается `AddDashboardPanels` на том же `IHostApplicationBuilder`;
- панели регистрируются в DI как singleton;
- появляется `GET /dashboard/panels` и
  `POST /dashboard/panels/{panel}/actions/{action}`;
- страница «Управление» показывает панели источников из registry /
  `Management:Sources`, поэтому для панелей хоста добавьте хост источником,
  если они должны отображаться в UI.

Для своих регистраций есть общий hook:

```csharp
.ConfigureHost(host => host.Services.AddSingleton<MyService>())
```

В демо-хосте добавлена панель `HostInfoPanel` («Хост»): версия, среда и
текущее время.

### FromConfiguration

Чтобы не дублировать чтение конфигурации, можно поднять все домены разом:

```csharp
builder.AddDashboard(dashboard => dashboard
    .FromConfiguration(builder.Configuration)
    .Branding(branding => branding.Title("РА-Монитор")));
```

`FromConfiguration` читает привычные ключи:

| Домен | Ключи |
|---|---|
| Branding | `Dashboard:Branding:Title`, `Dashboard:Branding:LogoUrl`, fallback `Dashboard:ApplicationName` |
| Hosting | `Dashboard:BasePath`, `Dashboard:Frontend:PublicUrl`, `ASPNETCORE_URLS` |
| Files | `Dashboard:Files:Title`, `Dashboard:Files:Roots:{id}:Path/Title/Url` |
| Otlp | `ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL`, `ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL`, `Dashboard:Otlp:*` |
| Management | `Management:TimeoutSeconds`, `Management:Sources:*` |
| Security | `ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS` |

Вызовы после `FromConfiguration` работают как override.

### Настройки хоста

Все значения по умолчанию — в `dashboard/host/appsettings.json`:

```json
{
  "AllowedHosts": "*",

  "ASPNETCORE_URLS": "http://0.0.0.0:18888",
  "ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL": "http://0.0.0.0:18889",
  "ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS": "true",

  "Dashboard": {
    "BasePath": "/",

    "Branding": {
      "Title": "РА-Монитор",
      "LogoUrl": "/img/logo-ra.svg"
    },

    "Frontend": {
      "PublicUrl": "http://localhost:18888"
    },

    "Otlp": {
      "SuppressUnsecuredMessage": true
    },

    "Files": {
      "Title": "Файлы",
      "Roots": {
        "logs": {
          "Title": "Логи",
          "Path": "/workspace/logs",
          "Url": "/files/logs"
        },
        "share": {
          "Title": "Share",
          "Path": "/host-home",
          "Url": "/files/share"
        }
      }
    }
  },

  "Management": {
    "TimeoutSeconds": 10
  },

  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

Переопределение через env:

```yaml
Dashboard__BasePath: /monitor
Dashboard__Frontend__PublicUrl: https://example.com/monitor
Dashboard__Branding__Title: РА-Монитор
Dashboard__Branding__LogoUrl: /img/logo-ra.svg
Dashboard__Files__Title: Файлы
Dashboard__Files__Roots__logs__Title: Логи
Dashboard__Files__Roots__logs__Path: /workspace/logs
Dashboard__Files__Roots__logs__Url: /files/logs
Dashboard__Files__Roots__share__Title: Share
Dashboard__Files__Roots__share__Path: /host-home
Dashboard__Files__Roots__share__Url: /files/share
ASPNETCORE_URLS: http://0.0.0.0:18888
ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL: http://0.0.0.0:18889
ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS: "true"
```

### Свой base URL у каждого хоста

Один и тот же образ можно смонтировать под разными путями:

```yaml
# хост A
Dashboard__BasePath: /
Dashboard__Frontend__PublicUrl: http://localhost:18888

# хост B
Dashboard__BasePath: /monitor
Dashboard__Frontend__PublicUrl: https://example.com/monitor
DASHBOARD_REGISTRY_URL: http://dashboard:18888/monitor/api/registry
```

Важно: при `BasePath != "/"` **все** endpoints дашборда, включая registry,
переезжают под base path. Приложения должны регистрироваться по адресу
с префиксом:

```text
http://aspire-dashboard:18888/monitor/api/registry
```

Если этого не сделать, Management останется пустым: приложения будут стучаться
в `/api/registry` и получать 404.

### Как встроить в другой хост

1. Добавить `ProjectReference` на `Aspire.Dashboard.csproj`.
2. Убедиться, что хост target'ит `net11.0` (исходники Aspire 17).
3. Включить `RequiresAspNetWebAssets=true`, если в хосте нет `.razor`-файлов —
   иначе не попадут `_framework/blazor.web.js` и другие framework assets.
4. Вызвать `AddDashboard(...)` и `UseDashboard()`.
5. Положить свой логотип в `wwwroot` и указать `LogoUrl`.
6. Задать `BasePath`/`PublicUrl`, если дашборд монтируется не в корень.

Пример `.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <RequiresAspNetWebAssets>true</RequiresAspNetWebAssets>
    <PublishAot>false</PublishAot>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../aspire/src/Aspire.Dashboard/Aspire.Dashboard.csproj" />
    <PackageReference Include="Dapper.AOT" PrivateAssets="all" Publish="true" />
    <PackageReference Include="Google.Protobuf" />
    <PackageReference Include="Azure.Core" />
  </ItemGroup>
</Project>
```

`Dapper.AOT` нужен хосту отдельно: в библиотеке он помечен `PrivateAssets=all`,
а сгенерированные интерсепторы требуют runtime-сборку. `Google.Protobuf` и
`Azure.Core` пинятся в `dashboard/host/Directory.Packages.props`, чтобы хост
не выбирал более старые транзитивные версии.

### Технические детали

- `dashboard/aspire` собирается как **библиотека** (`OutputType=Library`),
  его `Program.cs` удалён.
- `dashboard/host` — отдельный `Microsoft.NET.Sdk.Web` проект на `net11.0`.
- Хост не содержит `.razor`; компоненты берутся из библиотеки через
  `MapRazorComponents<App>()`.
- Все настройки библиотеки приходят из `WebApplicationBuilder` хоста;
  собственный `appsettings.json` библиотеки не публикуется.
- `MapStaticAssets()` обязателен: без него `_framework/blazor.web.js` не
  отдаётся при запуске из библиотеки.
- `UsePathBase` монтируется до явного `UseRouting`, иначе `_blazor` остаётся
  на корне.
- Docker собирает `dashboard/host`, а не `Aspire.Dashboard`.

### Версия хоста

Версия продукта принадлежит хосту и задаётся в `dashboard/host/RaMonitor.Host.csproj`:

```xml
<Version>1.0.0</Version>
<AssemblyVersion>1.0.0.0</AssemblyVersion>
<FileVersion>1.0.0.0</FileVersion>
<InformationalVersion>1.0.0</InformationalVersion>
```

Где используется:

- `InformationalVersion` — версия в стартовом логе и в HTML
  `<meta name="aspire-version">` (её читает UI и диагностика);
- `AssemblyVersion` — версия сборки для CLR/DI;
- `FileVersion` — файловая версия для Windows/артефактов;
- версия **панелей приложений** (`Счётчик app2 v10.0.2`) берётся из сборки
  самого приложения, а не из хоста.

Переопределение в CI без правки файла:

```bash
cd dashboard/host
dotnet publish RaMonitor.Host.csproj -c Release -p:PublishAot=false \
  -p:Version=1.2.3 -p:InformationalVersion=1.2.3
```

Docker-сборка принимает версию через build arg:

```bash
DASHBOARD_VERSION=1.2.3 docker compose build aspire-dashboard
```

`dashboard/Dockerfile` объявляет `ARG VERSION` и передаёт
`-p:Version=$VERSION` в `dotnet publish`. Без аргумента используется версия
из `.csproj`.

Если версия нужна в рантайме (например, для `/healthz` или собственного API),
читайте её из entry assembly:

```csharp
var version = Assembly.GetEntryAssembly()
    ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
    ?.InformationalVersion;
```

### Проверка

```bash
# корневой запуск
curl -I http://localhost:18888/management
curl -I http://localhost:18888/_framework/blazor.web.js
curl    http://localhost:18888/api/registry

# запуск под /monitor
curl -I http://localhost:18888/monitor/management
curl -I http://localhost:18888/monitor/_blazor/negotiate
curl    http://localhost:18888/monitor/api/registry
```

Ожидаемо: `200` для UI/статики, `302` для `/` (redirect на
`/structuredlogs`), список `app1`/`app2` в registry.

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

    public Task<DashboardContent> Apply() => Task.FromResult(
        DashboardContent.Markdown($"**Принято:** {Name}, {Count}"));

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
- результат действия показывается под панелью в соответствующем viewer
  (текст / JSON / Markdown);
- краткое событие уходит в **колокольчик** Aspire Dashboard
  (`INotificationService`), клик по событию открывает viewer.

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

### Результат действия

Вместо строки действие может вернуть `DashboardContent`:

```csharp
public Task<DashboardContent> Report() =>
    Task.FromResult(DashboardContent.Markdown("""
        ## Отчёт

        | Параметр | Значение |
        |---|---|
        | Шаг | **1** |
        """));

// короткие формы:
DashboardContent.Text("готово");
DashboardContent.Json(json);
DashboardContent.Markdown(markdown);
```

Ответ API:

```json
{
  "success": true,
  "content": {
    "content": "## Отчёт\n...",
    "type": "markdown"
  },
  "panel": { "...": "..." }
}
```

`type` по умолчанию — `text`. Поддерживаются `text`, `json`, `markdown`
(алиасы: `md`, `text/markdown`, `application/json`). Строка, возвращённая
методом действия, по-прежнему работает и трактуется как `text`; произвольный
объект сериализуется в JSON.

### Элементы панели

Кроме полей и кнопок панель может содержать статические элементы:

```csharp
.Panel<CounterPanel>("Счётчик app2")
    .Label("Живой счётчик приложения", sortOrder: 0)
    .Info("Значение обновляется каждую секунду.", sortOrder: 1)
    .Note("Настройки применяются сразу.", sortOrder: 2)
    .Warn("Сброс обнуляет счётчик.", sortOrder: 3)
    .Input(p => p.Step, "Шаг")
    .Action(p => p.Apply(), "Применить");
```

- `Label` — обычный текст;
- `Note` — нейтральный alert;
- `Info` — информационный alert;
- `Warn` — предупреждение.

Элементы приходят в `GET /dashboard/panels` в `elements` (`type`, `text`,
`order`) и показываются над полями панели. Компонент `PanelElements` общий
для «Управления» и «Поиска».

### Поисковые панели

`SearchPanel<T>(...)` — тот же контракт, что и `Panel<T>`, но панель
регистрируется на отдельной вкладке **«Поиск»**:

```csharp
builder.AddDashboardPanels(panels => panels
    .SearchPanel<CallSearchPanel>("Звонки", order: 20)
        .Input(p => p.Phone, "Телефон")
        .Action(p => p.Find(), "Найти")
    .SearchPanel<ImportSearchPanel>("Импорт", order: 21)
        .Input(p => p.DateFrom, "Дата с", DashboardInputType.Date)
        .Input(p => p.DateTo, "Дата по", DashboardInputType.Date)
        .Action(p => p.Find(), "Найти"));
```

В дескрипторе такая панель имеет `kind: "search"`, обычная — `kind: "panel"`.
«Управление» показывает только обычные панели, «Поиск» — только поисковые.

Вкладка «Поиск»:

- в тулбаре combo со списком фильтров (`источник · заголовок панели`);
- поля выбранного фильтра — в одну строку, Enter запускает поиск;
- кнопки: действие «Найти», локальный «Сброс», остальные действия панели;
- ниже — список ответов, новый сверху; каждый ответ это карточка
  с временем, длительностью, параметрами запроса и viewer-ом;
- «×» закрывает один ответ, «Очистить все» — весь список.

Состояние формы хранится в URL:

```text
/search?filter=app2%2FCallSearchPanel&Phone=999%20100
```

URL обновляется при смене фильтра, сбросе и запуске поиска (не во время
ввода), поэтому ссылка открывает форму с заполненными полями, а кнопка
«Назад» возвращает значения из истории.

### Viewers

Просмотр контента вынесен в `ContentViewer` + `ContentViewerRegistry`:

```text
Components/Content/
  ContentViewer.razor          # выбирает viewer по type
  ContentViewerRegistry.cs     # type -> component
  TextViewer.razor             # text + highlight.js для json/yaml
  JsonViewer.razor             # JSON
  MarkdownViewer.razor         # MarkdownRenderer (Markdig)
```

Один и тот же viewer используется:

- под панелью «Управление» после действия;
- в центре уведомлений при клике на событие;
- в файловом браузере для `.md`, `.json`, `.yaml` и текстовых файлов.

Новые типы добавляются без изменения UI:

```csharp
builder.AddDashboard(...)
    .ContentViewers(viewers => viewers
        .Register("html", typeof(HtmlViewer)));
```

Неизвестный `type` открывается как `text`.

### Уведомления

После действия создаётся краткое событие в колокольчике:

```text
app2 · Счётчик app2 · Отчёт (MD)
Счётчик app2
Нажмите, чтобы открыть
Just now
```

События упорядочены по времени. Клик по событию открывает viewer по `type`
(`text`, `json`, `markdown`), кнопка «Назад» возвращает к списку.

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
- **«Сбросить»** — `counter.Reset()`, с `RequireConfirm` (confirm в UI);
- **«Отчёт (MD)»** — возвращает `DashboardContent.Markdown`, показывает
  Markdown-viewer под панелью и открывается из уведомления.

Также у панели есть статические элементы: `Label`, `Info`, `Note`, `Warn`.

Панель **«Миграции»** (`MigrationPanel`) показывает громкий `Warn`-alert и
кнопку **«Миграция»**. Кнопка возвращает большой Markdown-отчёт-заглушку:

- PostgreSQL: таблицы, индексы, процедуры/функции, представления, права;
- Elasticsearch: индексный шаблон, mapping, анализатор, alias/reindex, ILM;
- порядок выполнения, проверки, риски, rollback и ссылки на документацию.

Поисковые панели app2 (вкладка «Поиск»):

- **«Звонки»** — поиск по номеру телефона, Markdown-таблица;
- **«Импорт»** — период `DateFrom`/`DateTo`, JSON-сводка;
- **«Накладная»** — поиск по номеру, Markdown-карточка.

Все три зарегистрированы через `SearchPanel<T>(...)` в
`src/App2.Counter/Program.cs`.

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

В навигации одна вкладка **«Файлы»**. Её заголовок задаётся
`Dashboard__Files__Title`, а корни — дочерними узлами `Dashboard__Files__Roots`.

```yaml
environment:
  Dashboard__Files__Title: Файлы
  Dashboard__Files__Roots__logs__Title: Логи
  Dashboard__Files__Roots__logs__Path: /workspace/logs
  Dashboard__Files__Roots__logs__Url: /files/logs
  Dashboard__Files__Roots__share__Title: Share
  Dashboard__Files__Roots__share__Path: /host-home
  Dashboard__Files__Roots__share__Url: /files/share
```

Корень выбирается в combo на тулбаре; у каждого корня свой URL:

```text
/files/logs    → /workspace/logs
/files/share   → /host-home
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
- скачивание: `/files/download?root=logs&path=app1.log`;
- просмотр UTF-8 до 5 МБ;
- `.md` открывается в Markdown-viewer, `.json`/`.yaml` — с подсветкой,
  остальные текстовые файлы — в текстовом viewer;
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
DASHBOARD_LOGO_URL=/img/logo-ra.svg
DASHBOARD_BASE_PATH=/
DASHBOARD_PUBLIC_URL=http://localhost:18888
DASHBOARD_REGISTRY_URL=http://aspire-dashboard:18888/api/registry
DASHBOARD_SUPPRESS_UNSECURED=true
DASHBOARD_FILES_TITLE=Файлы
DASHBOARD_LOGS_TITLE=Логи
DASHBOARD_LOGS_PATH=/workspace/logs
DASHBOARD_LOGS_URL=/files/logs
DASHBOARD_SHARE_TITLE=Share
DASHBOARD_SHARE_PATH=/host-home
DASHBOARD_SHARE_URL=/files/share

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

Ключевые переменные `aspire-dashboard` (host):

| Переменная | Назначение |
|---|---|
| `Dashboard__Branding__Title` | заголовок и имя приложения |
| `Dashboard__Branding__LogoUrl` | логотип в шапке |
| `Dashboard__BasePath` | путь монтирования (`/` или `/monitor`) |
| `Dashboard__Frontend__PublicUrl` | публичный URL для ссылок |
| `ASPNETCORE_URLS` | listen URL дашборда |
| `ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL` | OTLP/gRPC endpoint |
| `ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS` | unsecured-режим шага 1 |
| `Dashboard__Files__Title` | заголовок вкладки «Файлы» |
| `Dashboard__Files__Roots__{id}__Title` | название корня в combo |
| `Dashboard__Files__Roots__{id}__Path` | путь корня на диске |
| `Dashboard__Files__Roots__{id}__Url` | URL корня под `/files` |
| `Management__TimeoutSeconds` | таймаут запроса к источнику (10) |
| `Management__Sources__*` | статический fallback, если registry пуст |

Если `Dashboard__BasePath` не `/`, приложениям нужен
`DASHBOARD_REGISTRY_URL` с этим префиксом.

## Сборка

Приложения:

```bash
dotnet build OtelDemo.slnx -c Release
```

Dashboard-хост:

```bash
cd dashboard/host
dotnet run -c Release -p:PublishAot=false
```

Сборка без запуска:

```bash
cd dashboard/host
dotnet build RaMonitor.Host.csproj -c Release -p:PublishAot=false
```

Приложения на .NET 10. Для сборки dashboard и хоста нужен .NET 11 SDK, потому
что исходники Aspire 17 target `net11.0`. Панельки (`RaMonitor.Panels`)
мультитаргетят `net10.0;net11.0`: с .NET 10 SDK собирается только `net10.0`,
с .NET 11 SDK — оба таргета. Хост берёт настройки из
`dashboard/host/appsettings.json`; переменные окружения переопределяют их.

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
- **Нет вкладки «Файлы»** — не задан ни один `Dashboard__Files__Roots__{id}__Path`.
- **Нет JSON/YAML подсветки** — preview доступен только для UTF-8 файлов до 5 МБ.
- **Порт занят** — измените `DASHBOARD_PORT` / `APP*_PORT` в `.env`.

## Лицензия

Код проекта — MIT (`LICENSE`). Aspire Dashboard, на котором построен UI, —
MIT (`dashboard/aspire/LICENSE.TXT`). Уведомления третьих сторон —
`THIRD-PARTY-NOTICES.TXT` и `dashboard/aspire/THIRD-PARTY-NOTICES.TXT`.

Что приложить в зависимости от сценария (закрытый продукт, Docker, NuGet/npm,
SaaS, публичный форк) — [`docs/licensing.md`](docs/licensing.md).
