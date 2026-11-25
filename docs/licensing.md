# Лицензионные формальности

Код проекта — MIT (`LICENSE`). Aspire Dashboard — MIT
(`dashboard/aspire/LICENSE.TXT`). Зависимости перечислены в
`THIRD-PARTY-NOTICES.TXT` и `dashboard/aspire/THIRD-PARTY-NOTICES.TXT`.

## По сценариям

| Сценарий | Что приложить | Обязательно |
|---|---|---|
| Закрытый продукт / встраивание кода | `LICENSE`, `dashboard/aspire/LICENSE.TXT` | сохранить copyright и текст MIT |
| Docker-образ | `/app/licenses/LICENSE.txt`, `/app/licenses/THIRD-PARTY-NOTICES.txt`, `/app/licenses/ASPIRE-LICENSE.txt`, `/app/licenses/ASPIRE-THIRD-PARTY-NOTICES.txt` | сохранить notices при передаче образа |
| NuGet-пакет | `LICENSE`, `THIRD-PARTY-NOTICES.TXT` в пакете; `PackageLicenseExpression=MIT` | указать лицензию и notices |
| npm-пакет | `LICENSE`, `THIRD-PARTY-NOTICES.TXT`; `"license": "MIT"` | указать лицензию и notices |
| SaaS | ничего публиковать не нужно | сохранить notices в репозитории |
| Публичный форк | `LICENSE`, `NOTICE`, notices третьих сторон | сохранить MIT, добавить свой copyright |

## Что нельзя

- использовать названия и логотипы Microsoft, .NET, Aspire как свои;
- подразумевать официальную поддержку Microsoft.

Допустимая формулировка: «основан на Aspire Dashboard (MIT)».

## Проверка перед релизом

```bash
test -f LICENSE
test -f NOTICE
test -f THIRD-PARTY-NOTICES.TXT
test -f dashboard/aspire/LICENSE.TXT
test -f dashboard/aspire/THIRD-PARTY-NOTICES.TXT
docker run --rm --entrypoint ls <image> /app/licenses
```
