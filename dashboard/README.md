# Custom Aspire Dashboard

Официальный Aspire Dashboard не поддерживает плагины, поэтому для пункта
**«Управление»** собирается свой образ из исходников Aspire 17.

## Что лежит здесь

```text
dashboard/
  Dockerfile
  aspire/                    # vendored исходники Aspire v17.0.0
    src/Aspire.Dashboard/
      Components/Layout/ResourcesLayout.razor(.cs/.css)
      Components/Pages/Management.razor(.cs/.css)
    src/Shared/DashboardUrls.cs
```

`Dockerfile` собирает дашборд из локальной папки `aspire/`; в Docker ничего
не клонируется.

Приложения остаются на .NET 10. .NET 11 нужен только для сборки дашборда,
потому что исходники Aspire 17 target `net11.0`.

## Что изменено

| Файл | Изменение |
|---|---|
| `Components/Layout/ResourcesLayout.razor.cs` | новая `ResourceTab.Management`, вкладка после Metrics, URL/парсинг |
| `Shared/DashboardUrls.cs` | `ManagementBasePath` и `ManagementUrl()` |
| `Components/Pages/Management.razor` | страница внутри `resource-content` |
| `Components/Pages/Management.razor.cs` | загрузка `/app2/api/panels`, действия, фильтр |
| `Components/Pages/Management.razor.css` | стили на переменных Aspire Dashboard |
| `Components/Pages/FileBrowser.razor(.cs/.css)` | файловый браузер для вкладок «Логи» / «Share» |
| `Components/Files/FileBrowserTabService.cs` | опциональные корни вкладок из конфигурации/DI |
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

- запрашивает `GET /app2/api/panels` **на сервере** (`Management__ApiBaseUrl`);
- показывает фильтр **«Приложение»** — все / app1 / app2 / другие источники;
- клик по app в левом списке ресурсов открывает `/management/resource/{app}` и фильтрует панели;
- в заголовке каждой панели — версия приложения из assembly (`v1.0.0`, `v10.0.2`);
- рисует панели выбранного app;
- поддерживает text/date/int/bool;
- отправляет `POST /app2/api/panels/{source}/{panel}/actions/{action}`;
- показывает результат в колокольчике Aspire Dashboard (Notification Center).

Так как запрос идёт с сервера дашборда, страница работает и через gateway
(`:8080`), и при прямом открытии дашборда (`:18888`), если контейнер видит app2.

Настройка источника:

```yaml
environment:
  Management__ApiBaseUrl: http://app2:8080/api
```

## Фильтр по приложению

Список приложений строится из ответа `/api/panels`. При выборе app1 остаются
только панели app1; «Все приложения» показывает всё. Новый app появится в
фильтре автоматически после добавления в `Dashboard:Sources`.

## Файловый браузер «Логи» / «Share»

Вкладки появляются после «Управления» только если задан путь:

```yaml
environment:
  Dashboard__Files__Logs__Path: /workspace/logs
  Dashboard__Files__Share__Path: /workspace/share
```

Корень монтируется read-only:

```yaml
volumes:
  - ./:/workspace:ro
```

`FileBrowserTabService` регистрируется в DI и отдаёт только настроенные вкладки.
`FileBrowser.razor` умеет:

- маски `*.log`, `*.txt;*.json`;
- папки выше файлов;
- колонки: icon, Name, Size, Type, Date Modified, view;
- вход в папку, «Вверх», «В корень», клавиша Home;
- download через `/files/download`;
- preview UTF-8 до 5 МБ в полноэкранном режиме с кнопками back/download;
- подсветка JSON/YAML (highlight.js);
- защиту от path traversal.

Скриншот: `../docs/file-browser.png`.
