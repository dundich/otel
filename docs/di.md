# DI-регистрация dashboard

Документ описывает, как подключить `Aspire.Dashboard` к своему web-хосту
через DI: где находится рабочий пример, что регистрируется, в каком порядке
вызывать методы и как передавать настройки по доменам.

## Где пример

Рабочий пример в репозитории:

```text
dashboard/host/Program.cs          # регистрация AddDashboard/UseDashboard
dashboard/host/appsettings.json    # все настройки хоста
dashboard/host/RaMonitor.Host.csproj
dashboard/aspire/src/Aspire.Dashboard/DashboardHostExtensions.cs
dashboard/aspire/src/Aspire.Dashboard/DashboardBuilder.cs
```

`Program.cs` целиком:

```csharp
using Aspire.Dashboard;
using RaMonitor.Host;

var builder = WebApplication.CreateBuilder(args);

builder.AddDashboard(dashboard => dashboard
    .Branding(branding => branding
        .Title(builder.Configuration["Dashboard:Branding:Title"] ?? "РА-Монитор")
        .LogoUrl(builder.Configuration["Dashboard:Branding:LogoUrl"] ?? "/img/logo-ra.svg"))
    .Hosting(hosting => hosting
        .BasePath(builder.Configuration["Dashboard:BasePath"] ?? "/")
        .PublicUrl(builder.Configuration["Dashboard:Frontend:PublicUrl"])
        .EndpointUrls(builder.Configuration["ASPNETCORE_URLS"]))
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
    .Otlp(otlp => otlp
        .GrpcEndpoint(builder.Configuration["ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL"])
        .HttpEndpoint(builder.Configuration["ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL"])
        .SuppressUnsecuredMessage(builder.Configuration.GetValue(
            "Dashboard:Otlp:SuppressUnsecuredMessage", true)))
    .Management(management => management
        .Timeout(builder.Configuration.GetValue("Management:TimeoutSeconds", 10)))
    .Security(security => security
        .AllowAnonymous(builder.Configuration.GetValue(
            "ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS", true)))
    .Panels(panels => panels.ScanAssembly<HostInfoPanel>()));

var app = builder.Build();

app.UseDashboard();

app.Run();
```

## Минимальный вариант

Если настройки лежат в привычных ключах, достаточно одной строки:

```csharp
using Aspire.Dashboard;

var builder = WebApplication.CreateBuilder(args);

builder.AddDashboard(dashboard => dashboard
    .FromConfiguration(builder.Configuration));

var app = builder.Build();

app.UseDashboard();

app.Run();
```

`FromConfiguration` читает `Dashboard:*`, `Management:*` и стандартные
`ASPNETCORE_URLS` / `ASPIRE_DASHBOARD_*`. Отдельные домены можно переопределить
после него:

```csharp
builder.AddDashboard(dashboard => dashboard
    .FromConfiguration(builder.Configuration)
    .Branding(branding => branding
        .Title("РА-Монитор")
        .LogoUrl("/img/logo-ra.svg")));
```

## Что делает AddDashboard

`AddDashboard()` — extension для `WebApplicationBuilder`. Он:

1. Создаёт `DashboardBuilder` и применяет переданные домены.
2. Переносит значения в `IConfiguration` через in-memory provider. Провайдер
   добавляется последним, поэтому значения билдера выигрывают у env.
3. Создаёт `DashboardComponentOptions` (base path, public URL, title, logo).
4. Включает static web assets для запуска из исходников вне Development.
5. Создаёт `DashboardWebApplication`.
6. Регистрирует в DI:
   - `DashboardComponentOptions` — singleton;
   - `DashboardWebApplication` — singleton;
   - все сервисы dashboard: Blazor Server, Fluent UI, OTLP, telemetry,
     Management, file browser, auth, antiforgery, localization.
7. Настраивает Kestrel endpoints из `Hosting.EndpointUrls` и OTLP endpoint.

Важно: `AddDashboard()` не создаёт `WebApplication` и не запускает сервер.
Это делает хост.

## Что делает UseDashboard

`UseDashboard()` вызывается после `builder.Build()`:

```csharp
var app = builder.Build();
app.UseDashboard();
app.Run();
```

Он:

1. Достаёт `DashboardWebApplication` из DI.
2. Монтирует `BasePath` через `UsePathBase`.
3. Добавляет явный `UseRouting` после `UsePathBase` — иначе Blazor endpoint
   `_blazor` останется в корне хоста.
4. Настраивает localization, CORS, forwarded headers, auth, antiforgery.
5. Подключает статику и `MapStaticAssets()`.
6. Маппит:
   - Blazor-компоненты (`MapRazorComponents<App>()`);
   - OTLP/gRPC и OTLP/HTTP;
   - telemetry API;
   - registry;
   - health checks;
   - файловый браузер.

Порядок middleware важен: auth идёт после forwarded headers/CORS,
antiforgery — после authorization, OTLP endpoints не пересекаются с UI.

## Домены настроек

`DashboardBuilder` группирует настройки по доменным понятиям.

### Branding

```csharp
.Branding(branding => branding
    .Title("РА-Монитор")
    .LogoUrl("/img/logo-ra.svg"))
```

| Метод | Ключ конфигурации | Назначение |
|---|---|---|
| `Title` | `Dashboard:Branding:Title` | заголовок, имя приложения, `<title>` |
| `LogoUrl` | `Dashboard:Branding:LogoUrl` | логотип в шапке |

Если `LogoUrl` начинается с `/`, base path добавляется автоматически.
Абсолютный URL (`https://cdn/logo.svg`) не переписывается.

### Hosting

```csharp
.Hosting(hosting => hosting
    .BasePath("/monitor")
    .PublicUrl("https://example.com/monitor")
    .EndpointUrls("http://0.0.0.0:18888"))
```

| Метод | Ключ конфигурации | Назначение |
|---|---|---|
| `BasePath` | `Dashboard:BasePath` | путь монтирования |
| `PublicUrl` | `Dashboard:Frontend:PublicUrl` | публичный URL для ссылок |
| `EndpointUrls` | `ASPNETCORE_URLS` | listen URL Kestrel |

### Files

Одна вкладка **«Файлы»**, внутри — выбираемые корни. Билдер вложенный:
`Files.Title` задаёт название вкладки, `Root` — дочерний элемент combo.

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

| Метод | Ключи конфигурации |
|---|---|
| `Title(title)` | `Dashboard:Files:Title` |
| `Root(id, root => ...)` | дочерний узел: `.Title`, `.Path`, `.Url` |
| `Roots(roots => ...)` | вызвать `roots.Root(...)` для каждого узла |
| `Root(id, path, title?, url?)` | сокращённая запись без вложенного билдера |
| `Logs(path, title?, url?)` | сокращение для `Root("logs", ...)` |
| `Share(path, title?, url?)` | сокращение для `Root("share", ...)` |

У дочернего билдера есть также `FromConfiguration(configuration)` — он читает
`Dashboard:Files:Roots:{id}`.

`null`-путь удаляет корень. `Url` по умолчанию — `/files/{id}`. В UI корень
выбирается в combo на тулбаре, URL меняется на `/files/logs` или
`/files/share`.

### Otlp

```csharp
.Otlp(otlp => otlp
    .GrpcEndpoint("http://0.0.0.0:18889")
    .HttpEndpoint("http://0.0.0.0:18890")
    .Unsecured()
    .SuppressUnsecuredMessage(true))
```

| Метод | Ключ конфигурации |
|---|---|
| `GrpcEndpoint` | `ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL` |
| `HttpEndpoint` | `ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL` |
| `Unsecured` | `Dashboard:Otlp:AuthMode=Unsecured` |
| `SuppressUnsecuredMessage` | `Dashboard:Otlp:SuppressUnsecuredMessage` |

### Management

```csharp
.Management(management => management
    .Timeout(10)
    .Source("app1", "http://app1:8080", "http://localhost:8081"))
```

| Метод | Ключи конфигурации |
|---|---|
| `Timeout` | `Management:TimeoutSeconds` |
| `Source` | `Management:Sources:{n}:Name/Url/PublicUrl` |

`Source` — статический fallback. Основной путь — registry: приложения сами
регистрируют свой internal URL.

### Security

```csharp
.Security(security => security.AllowAnonymous(true))
```

| Метод | Ключ конфигурации |
|---|---|
| `AllowAnonymous` | `ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS` |

### Panels

`Panels` — хостовое расширение над существующим SDK `RaMonitor.Panels`.
Оно вызывает `AddDashboardPanels` на том же `IHostApplicationBuilder`, поэтому
панели попадают в тот же DI:

```csharp
builder.AddDashboard(dashboard => dashboard
    ...
    .Panels(panels => panels
        .Panel<HostInfoPanel>("Хост", order: 100)
            .Input(p => p.Version, "Версия", DashboardInputType.ReadOnly)));
```

| Метод | Что делает |
|---|---|
| `Panels(panels => ...)` | `AddDashboardPanels` на host builder |
| `ConfigureHost(host => ...)` | произвольная регистрация в DI хоста |

После регистрации:

```text
GET  /dashboard/panels
POST /dashboard/panels/{panel}/actions/{action}
```

Чтобы эти панели отображались на странице «Управление», хост должен быть
источником в registry или `Management:Sources`.

### ConfigureHost

Общий hook для регистраций, которым нужен тот же `IHostApplicationBuilder`:

```csharp
.ConfigureHost(host => host.Services.AddSingleton<MyService>())
```

`Panels` построен на этом hook.

## DI и lifetime

| Сервис | Lifetime | Кто создаёт |
|---|---|---|
| `DashboardWebApplication` | Singleton | `AddDashboard()` |
| `DashboardComponentOptions` | Singleton | `AddDashboard()` |
| `FileBrowserRootService` | Singleton | `AddDashboard()` |
| Blazor circuits | Scoped | ASP.NET Core |
| OTLP/telemetry services | Singleton/Scoped | регистрация dashboard |
| Панели приложений | Singleton | `AddDashboardPanels` в приложении |
| Панели хоста | Singleton | `Panels(...)` / `AddDashboardPanels` в хосте |

`AddDashboard()` вызывается до `builder.Build()`. `UseDashboard()` — после.
Повторный вызов `AddDashboard()` не предусмотрен: это одна точка подключения
компонента.

## Настройки хоста

Все значения по умолчанию — в `dashboard/host/appsettings.json`:

```json
{
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

## Встраивание в существующий хост

1. Добавить `ProjectReference`:

```xml
<ProjectReference Include="../aspire/src/Aspire.Dashboard/Aspire.Dashboard.csproj" />
```

2. Target framework — `net11.0`.

3. Если в хосте нет `.razor`-файлов, включить framework assets:

```xml
<RequiresAspNetWebAssets>true</RequiresAspNetWebAssets>
```

4. Добавить runtime-зависимости, которые в библиотеке помечены `PrivateAssets`:

```xml
<PackageReference Include="Dapper.AOT" PrivateAssets="all" Publish="true" />
<PackageReference Include="Google.Protobuf" />
<PackageReference Include="Azure.Core" />
```

5. Вызвать `AddDashboard()`/`UseDashboard()`.

6. Положить логотип в `wwwroot` и указать `LogoUrl`.

7. При монтировании не в корень задать `BasePath` и `PublicUrl`.

## Base path и registry

При `BasePath != "/"` все endpoints, включая registry, переезжают под префикс:

```text
/monitor/management
/monitor/files/logs
/monitor/files/share
/monitor/api/registry
/monitor/_blazor/negotiate
```

Поэтому приложения должны регистрироваться по адресу с префиксом:

```yaml
Dashboard__RegistryUrl: http://aspire-dashboard:18888/monitor/api/registry
```

Иначе registry будет пустым: приложения получат 404 на `/api/registry`.

## Версия хоста

Версия задаётся в `dashboard/host/RaMonitor.Host.csproj`:

```xml
<Version>1.0.0</Version>
<AssemblyVersion>1.0.0.0</AssemblyVersion>
<FileVersion>1.0.0.0</FileVersion>
<InformationalVersion>1.0.0</InformationalVersion>
```

`InformationalVersion` показывается в стартовом логе и в HTML
`<meta name="aspire-version">`. Переопределение:

```bash
dotnet publish RaMonitor.Host.csproj -p:Version=1.2.3
DASHBOARD_VERSION=1.2.3 docker compose build aspire-dashboard
```

## Проверка

```bash
curl -I http://localhost:18888/management
curl -I http://localhost:18888/_framework/blazor.web.js
curl    http://localhost:18888/api/registry
```

Ожидаемо: `200` для UI и статики, `302` для `/` (redirect на
`/structuredlogs`), `app1`/`app2` в registry.
