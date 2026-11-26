# Custom Aspire Dashboard

Официальный Aspire Dashboard не поддерживает плагины, поэтому для пункта
**«Управление»** собирается свой образ из исходников Aspire 17.

## Что лежит здесь

```text
dashboard/
  Dockerfile
  host/                       # web-хост RaMonitor.Host: Program.cs, appsettings, wwwroot
  panels/                     # «Панельки»: SDK панелей приложений (RaMonitor.Panels)
  aspire/                     # vendored исходники Aspire v17.0.0
    src/Aspire.Dashboard/
      Components/Layout/ResourcesLayout.razor(.cs/.css)
      Components/Pages/Management.razor(.cs/.css)
    src/Shared/DashboardUrls.cs
```

`Dockerfile` собирает дашборд из локальной папки `aspire/`; в Docker ничего
не клонируется.

Приложения остаются на .NET 10. .NET 11 нужен для сборки дашборда, потому что
исходники Aspire 17 target `net11.0`. Панельки (`RaMonitor.Panels`)
мультитаргетят `net10.0;net11.0`; с .NET 10 SDK собирается только `net10.0`,
с .NET 11 SDK — оба таргета.

## Что изменено

| Файл | Изменение |
|---|---|
| `Components/Layout/ResourcesLayout.razor.cs` | вкладки `Management`, `Search`, `Files` после Metrics, URL/парсинг |
| `Shared/DashboardUrls.cs` | `ManagementBasePath`/`ManagementUrl()`, `SearchBasePath`/`SearchUrl()` |
| `Components/Pages/Management.razor` | страница внутри `resource-content` |
| `Components/Pages/Management.razor.cs` | UI страницы: форма, фильтр, действия, уведомления |
| `Management/*` | домен: `ManagementOptions` (`Management:Sources`), `ManagementModels` (DTO), `ManagementPanelService` (агрегация и действия), регистрация DI |
| `Components/Pages/Management.razor.css` | стили на переменных Aspire Dashboard |
| `Components/Pages/FileBrowser.razor(.cs/.css)` | одна вкладка «Файлы» с выбором корня в combo |
| `Components/Pages/Search.razor(.cs/.css)` | вкладка «Поиск»: фильтры, URL-состояние, список ответов |
| `Components/Controls/PanelElements.razor(.css)` | Label / Note / Info / Warn в теле панели |
| `Components/Files/FileBrowserRootService.cs` | корни файлового браузера: title, path, url из конфигурации/DI |
| `Components/Content/*` | ContentViewer + реестр viewers: text, json, markdown |
| `Components/Dialogs/NotificationsDialog.*` | список событий по времени и viewer по клику |
| `Components/Controls/AspireLogo.razor` | логотип заменён на `/img/logo-purple.png` |
| `wwwroot/img/logo-purple.png` | логотип из `cloud-sa.vsrobotics.ru` |

## Сборка

Локально (нужен .NET 11 SDK):

```bash
cd dashboard/aspire
dotnet build src/Aspire.Dashboard/Aspire.Dashboard.csproj -c Release -p:PublishAot=false
```

Docker:

```bash
docker compose build aspire-dashboard
docker compose up -d
```

## Страница «Управление»

Ссылка появляется в настоящей шапке Aspire Dashboard после вкладки **Metrics**:

```text
http://localhost:8080/management
```

Страница рендерится внутри `resource-content`, как Traces/Metrics, и сохраняет
CSS/тему дашборда.

Она:

- запрашивает панели **на сервере** напрямую у каждого приложения из
  `Management:Sources` (`GET {source}/dashboard/panels`);
- показывает фильтр **«Приложение»** — все / app1 / app2;
- клик по app в левом списке ресурсов открывает `/management/resource/{app}` и фильтрует панели;
- в заголовке каждой панели — версия приложения из assembly (`v1.0.0`, `v10.0.2`);
- рисует панели выбранного app;
- поддерживает text/date/int/bool;
- отправляет `POST {source}/dashboard/panels/{panel}/actions/{action}` напрямую;
- показывает результат действия под панелью в подходящем viewer
  (text / JSON / Markdown);
- событие в колокольчике открывает тот же viewer по клику;
- показывает результат в колокольчике Aspire Dashboard (Notification Center).

Так как запрос идёт с сервера дашборда, страница работает и через gateway
(`:8080`), и при прямом открытии дашборда (`:18888`), если контейнер видит источники.

Настройка источников (дашборд знает их сам, промежуточного агрегатора нет):

```yaml
environment:
  Management__Sources__0__Name: app1
  Management__Sources__0__Url: http://app1:8080
  Management__Sources__0__PublicUrl: http://localhost:8081
  Management__Sources__1__Name: app2
  Management__Sources__1__Url: http://app2:8080
  Management__Sources__1__PublicUrl: http://localhost:8082
  # опционально: Management__TimeoutSeconds: 10
```

## Просмотр контента

Результат действия панели — `DashboardContent { content, type }`. Тип по
умолчанию `text`; поддерживаются `text`, `json`, `markdown`.

`ContentViewer` выбирает компонент по типу через `ContentViewerRegistry`:

```text
text      -> TextViewer
json      -> JsonViewer
markdown  -> MarkdownViewer
```

Тот же viewer используется под панелью «Управление», в центре уведомлений и в
файловом браузере. Новый тип добавляется одной регистрацией:

```csharp
.ContentViewers(viewers => viewers.Register("html", typeof(HtmlViewer)))
```

В демо app2 есть действие **«Отчёт (MD)»**: оно возвращает Markdown, который
открывается под панелью и из уведомления.

Панель **«Миграции»** (`MigrationPanel`) демонстрирует громкий `Warn`-alert и
кнопку **«Миграция»**: она возвращает большой Markdown-отчёт-заглушку с
таблицами и процедурами PostgreSQL, mapping/reindex для Elasticsearch, планом
выполнения, проверками и откатом.

## Элементы панели

Панель может содержать не только поля и кнопки, но и статические элементы:

```csharp
.Panel<CounterPanel>("Счётчик app2")
    .Label("Живой счётчик приложения")
    .Info("Значение обновляется каждую секунду.")
    .Note("Настройки применяются сразу.")
    .Warn("Сброс обнуляет счётчик.")
```

`Label` — текст, `Note`/`Info`/`Warn` — alerts с разным оформлением. Они
приходят в `elements` дескриптора и рендерятся компонентом `PanelElements`
над полями панели.

## Вкладка «Поиск»

Поисковые панели регистрируются через `SearchPanel<T>(...)`:

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

Такие панели имеют `kind: "search"` и не попадают на «Управление».

На вкладке:

- combo фильтров (`источник · панель`);
- поля выбранного фильтра в одну строку, Enter запускает поиск;
- «Найти», «Сброс» и остальные действия;
- список ответов, новый сверху: время, длительность, параметры запроса,
  viewer; «×» закрывает ответ, «Очистить все» очищает список.

Выбранный фильтр и значения полей пишутся в URL при поиске и смене фильтра
(не во время ввода):

```text
/search?filter=app2%2FCallSearchPanel&Phone=999%20100
```

Ссылка открывает форму с заполненными полями, «Назад» возвращает значения
из истории.

## Панели хоста

Хостовое расширение `Panels` переиспользует `RaMonitor.Panels` и
`AddDashboardPanels` в цепочке `AddDashboard`:

```csharp
builder.AddDashboard(dashboard => dashboard
    ...
    .Panels(panels => panels.ScanAssembly<HostInfoPanel>()));
```

После этого хост отдаёт:

```text
GET  /dashboard/panels
POST /dashboard/panels/{panel}/actions/{action}
```

В демо-хосте зарегистрирована панель `HostInfoPanel` («Хост»): версия, среда,
время. Чтобы она была видна на странице «Управление», хост нужно добавить
источником в registry или `Management:Sources`.

## Фильтр по приложению

Список приложений строится из `Management:Sources`. При выборе app1 остаются
только панели app1; «Все приложения» показывает всё. Новый app появится в
фильтре после добавления ещё одной пары `Management__Sources__N__*`.

## Файловый браузер «Файлы»

В навигации одна вкладка **«Файлы»** после «Управления». Её заголовок и
дочерние корни задаются в DI/конфигурации:

```yaml
environment:
  Dashboard__Files__Title: Файлы
  Dashboard__Files__Roots__logs__Title: Логи
  Dashboard__Files__Roots__logs__Path: /workspace/logs
  Dashboard__Files__Roots__logs__Url: /files/logs
  Dashboard__Files__Roots__share__Title: Share
  Dashboard__Files__Roots__share__Path: /workspace/share
  Dashboard__Files__Roots__share__Url: /files/share
```

Корень выбирается в combo на тулбаре. У каждого корня свой URL, например
`/files/logs` и `/files/share`; ссылка открывает сразу нужный корень.

Корень монтируется read-only:

```yaml
volumes:
  - ./:/workspace:ro
```

`FileBrowserRootService` регистрируется в DI и отдаёт только настроенные
корни. `FileBrowser.razor` умеет:

- выбирать корень в combo (меняет URL страницы);
- маски `*.log`, `*.txt;*.json`;
- папки выше файлов;
- колонки: icon, Name, Size, Type, Date Modified, view;
- вход в папку, «Вверх», «В корень», клавиша Home;
- download через `/files/download?root=logs&path=...`;
- preview UTF-8 до 5 МБ в полноэкранном режиме с кнопками back/download;
- подсветка JSON/YAML (highlight.js);
- защиту от path traversal.

Скриншот: `../docs/file-browser.png`.
