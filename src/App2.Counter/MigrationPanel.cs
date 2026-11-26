using RaMonitor.Panels;

namespace App2.Counter;

/// <summary>
/// Демонстрационная панель миграций. Кнопка «Миграция» не меняет состояние
/// приложения — она возвращает большой Markdown-отчёт с примером списка
/// миграций для PostgreSQL и Elasticsearch.
/// </summary>
public sealed class MigrationPanel : IDashboardPanel
{
    // Демонстрационная задержка: в UI кнопка «Миграция» показывает спиннер
    // и остаётся disabled, пока отчёт формируется.
    public async Task<DashboardContent> Report(CancellationToken cancellationToken = default)
    {
        await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken).ConfigureAwait(false);
        return DashboardContent.Markdown(BuildReport());
    }

    public async Task<object?> ExecuteAsync(string actionName, CancellationToken cancellationToken = default)
        => actionName switch
        {
            nameof(Report) => await Report(cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException(
                $"Action '{actionName}' is not handled by {nameof(MigrationPanel)}.")
        };

    private static string BuildReport() => $$"""
        # Отчёт по миграциям

        > **Окружение:** staging · **Набор:** `2026.11.26-01` · **Сформирован:** {{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}}

        ## 1. Сводка

        | Компонент | Версия | Объектов | Действие | Статус |
        |---|---:|---:|---|---|
        | PostgreSQL | 16.4 | 14 | DDL + процедуры | готово к применению |
        | Elasticsearch | 8.15.2 | 6 | mapping + reindex | требуется reindex |
        | Redis | 7.2.5 | 0 | без изменений | пропущено |

        **Итого:** 2 компонента, 23 объекта, 0 ошибок валидации. Ожидаемое окно
        работ — 35–50 минут, простой PostgreSQL — до 2 минут.

        ---

        ## 2. PostgreSQL 16

        ### 2.1. Новые таблицы

        | # | Схема | Объект | Действие | Оценка |
        |---:|---|---|---|---:|
        | 1 | `billing` | `invoice` | create table | 1 с |
        | 2 | `billing` | `invoice_line` | create table | 1 с |
        | 3 | `billing` | `payment` | create table | 1 с |
        | 4 | `audit` | `schema_change` | create table | 1 с |

        ```sql
        CREATE SCHEMA IF NOT EXISTS billing;
        CREATE SCHEMA IF NOT EXISTS audit;

        CREATE TABLE billing.invoice (
            id            bigserial PRIMARY KEY,
            number        text        NOT NULL UNIQUE,
            client_id     bigint      NOT NULL,
            issued_at     date        NOT NULL DEFAULT current_date,
            due_at        date,
            total_amount  numeric(14, 2) NOT NULL DEFAULT 0,
            currency      char(3)     NOT NULL DEFAULT 'RUB',
            status        text        NOT NULL DEFAULT 'draft'
                          CHECK (status IN ('draft', 'issued', 'paid', 'void')),
            created_at    timestamptz NOT NULL DEFAULT now(),
            updated_at    timestamptz NOT NULL DEFAULT now()
        );

        CREATE TABLE billing.invoice_line (
            id          bigserial PRIMARY KEY,
            invoice_id  bigint      NOT NULL
                        REFERENCES billing.invoice (id) ON DELETE CASCADE,
            sku         text        NOT NULL,
            quantity    numeric(12, 3) NOT NULL CHECK (quantity > 0),
            price       numeric(14, 2) NOT NULL CHECK (price >= 0),
            amount      numeric(14, 2) GENERATED ALWAYS AS (quantity * price) STORED
        );

        CREATE TABLE billing.payment (
            id          bigserial PRIMARY KEY,
            invoice_id  bigint      NOT NULL
                        REFERENCES billing.invoice (id),
            paid_at     timestamptz NOT NULL DEFAULT now(),
            amount      numeric(14, 2) NOT NULL CHECK (amount > 0),
            method      text        NOT NULL DEFAULT 'card'
        );

        CREATE TABLE audit.schema_change (
            id          bigserial PRIMARY KEY,
            version     text        NOT NULL,
            applied_at  timestamptz NOT NULL DEFAULT now(),
            applied_by  text        NOT NULL,
            duration_ms integer     NOT NULL,
            checksum    text        NOT NULL
        );
        ```

        ### 2.2. Изменения существующих таблиц

        | # | Таблица | Изменение | Блокировка |
        |---:|---|---|---|
        | 5 | `public.client` | добавить `external_id uuid` | `ACCESS EXCLUSIVE` |
        | 6 | `public.client` | уникальный индекс по `external_id` | `SHARE` |
        | 7 | `public.order` | добавить `invoice_id bigint` | `ACCESS EXCLUSIVE` |
        | 8 | `public.order` | backfill `invoice_id` батчами | без блокировки |

        ```sql
        ALTER TABLE public.client
            ADD COLUMN IF NOT EXISTS external_id uuid;

        CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS ux_client_external_id
            ON public.client (external_id)
            WHERE external_id IS NOT NULL;

        ALTER TABLE public."order"
            ADD COLUMN IF NOT EXISTS invoice_id bigint;

        -- backfill выполняется приложением батчами по 10 000 строк,
        -- чтобы не держать долгую транзакцию.
        ```

        ### 2.3. Индексы и ограничения

        ```sql
        CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_invoice_client_status
            ON billing.invoice (client_id, status);

        CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_invoice_issued_at
            ON billing.invoice (issued_at DESC);

        CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_invoice_line_invoice
            ON billing.invoice_line (invoice_id);

        ALTER TABLE billing.invoice
            ADD CONSTRAINT fk_invoice_client
            FOREIGN KEY (client_id) REFERENCES public.client (id)
            NOT VALID;

        ALTER TABLE billing.invoice VALIDATE CONSTRAINT fk_invoice_client;
        ```

        ### 2.4. Процедуры и функции

        | # | Объект | Назначение |
        |---:|---|---|
        | 9 | `billing.close_period(date)` | закрытие периода и выставление счетов |
        | 10 | `billing.recalc_invoice(bigint)` | пересчёт суммы по строкам |
        | 11 | `audit.log_schema_change(text, text, integer)` | журнал миграций |
        | 12 | `public.touch_updated_at()` | триггер обновления `updated_at` |

        ```sql
        CREATE OR REPLACE PROCEDURE billing.close_period(p_month date)
        LANGUAGE plpgsql
        AS $$
        DECLARE
            v_invoice_id bigint;
        BEGIN
            FOR v_invoice_id IN
                SELECT id
                FROM billing.invoice
                WHERE status = 'draft'
                  AND issued_at < date_trunc('month', p_month)
                FOR UPDATE SKIP LOCKED
            LOOP
                PERFORM billing.recalc_invoice(v_invoice_id);

                UPDATE billing.invoice
                   SET status = 'issued',
                       updated_at = now()
                 WHERE id = v_invoice_id;
            END LOOP;
        END;
        $$;

        CREATE OR REPLACE FUNCTION billing.recalc_invoice(p_invoice_id bigint)
        RETURNS numeric
        LANGUAGE plpgsql
        AS $$
        DECLARE
            v_total numeric;
        BEGIN
            SELECT coalesce(sum(amount), 0)
              INTO v_total
              FROM billing.invoice_line
             WHERE invoice_id = p_invoice_id;

            UPDATE billing.invoice
               SET total_amount = v_total,
                   updated_at = now()
             WHERE id = p_invoice_id;

            RETURN v_total;
        END;
        $$;

        CREATE OR REPLACE FUNCTION public.touch_updated_at()
        RETURNS trigger
        LANGUAGE plpgsql
        AS $$
        BEGIN
            NEW.updated_at := now();
            RETURN NEW;
        END;
        $$;

        CREATE OR REPLACE TRIGGER tr_invoice_touch
            BEFORE UPDATE ON billing.invoice
            FOR EACH ROW
            EXECUTE FUNCTION public.touch_updated_at();
        ```

        ### 2.5. Представления

        ```sql
        CREATE OR REPLACE VIEW billing.v_invoice_summary AS
        SELECT i.id,
               i.number,
               i.status,
               i.total_amount,
               i.currency,
               count(l.id) AS lines,
               coalesce(sum(p.amount), 0) AS paid
          FROM billing.invoice i
          LEFT JOIN billing.invoice_line l ON l.invoice_id = i.id
          LEFT JOIN billing.payment p ON p.invoice_id = i.id
         GROUP BY i.id;
        ```

        ### 2.6. Права доступа

        ```sql
        GRANT USAGE ON SCHEMA billing TO app_rw;
        GRANT SELECT, INSERT, UPDATE, DELETE
            ON ALL TABLES IN SCHEMA billing TO app_rw;
        GRANT USAGE, SELECT
            ON ALL SEQUENCES IN SCHEMA billing TO app_rw;
        GRANT EXECUTE
            ON ALL PROCEDURES IN SCHEMA billing TO app_rw;

        REVOKE ALL ON audit.schema_change FROM app_rw;
        GRANT INSERT ON audit.schema_change TO app_rw;
        ```

        ---

        ## 3. Elasticsearch 8.15

        ### 3.1. Индексный шаблон

        ```json
        PUT _index_template/orders-template
        {
          "index_patterns": ["orders-v*"],
          "priority": 500,
          "template": {
            "settings": {
              "number_of_shards": 3,
              "number_of_replicas": 1,
              "refresh_interval": "5s",
              "analysis": {
                "analyzer": {
                  "orders_text": {
                    "type": "custom",
                    "tokenizer": "standard",
                    "filter": ["lowercase", "russian_stop", "russian_stemmer"]
                  }
                },
                "filter": {
                  "russian_stop": { "type": "stop", "stopwords": "_russian_" },
                  "russian_stemmer": { "type": "stemmer", "language": "russian" }
                }
              }
            },
            "mappings": {
              "dynamic": "strict",
              "properties": {
                "order_id":    { "type": "keyword" },
                "client_id":   { "type": "keyword" },
                "number":      { "type": "keyword" },
                "status":      { "type": "keyword" },
                "created_at":  { "type": "date" },
                "paid_at":     { "type": "date" },
                "total":       { "type": "scaled_float", "scaling_factor": 100 },
                "comment":     { "type": "text", "analyzer": "orders_text" },
                "items": {
                  "type": "nested",
                  "properties": {
                    "sku":      { "type": "keyword" },
                    "title":    { "type": "text", "analyzer": "orders_text" },
                    "quantity": { "type": "integer" },
                    "price":    { "type": "scaled_float", "scaling_factor": 100 }
                  }
                }
              }
            }
          }
        }
        ```

        ### 3.2. Mapping индекса `orders-v2`

        | # | Поле | Тип | Изменение |
        |---:|---|---|---|
        | 13 | `invoice_id` | `keyword` | новое поле |
        | 14 | `items.amount` | `scaled_float` | новое поле |
        | 15 | `comment` | `text` + `orders_text` | новый анализатор |
        | 16 | `updated_at` | `date` | новое поле |

        ```json
        PUT orders-v2/_mapping
        {
          "properties": {
            "invoice_id": { "type": "keyword" },
            "updated_at": { "type": "date" },
            "items": {
              "type": "nested",
              "properties": {
                "amount": { "type": "scaled_float", "scaling_factor": 100 }
              }
            }
          }
        }
        ```

        ### 3.3. Alias и reindex

        ```json
        POST _reindex?wait_for_completion=false
        {
          "source": { "index": "orders-v1" },
          "dest":   { "index": "orders-v2", "op_type": "create" },
          "script": {
            "source": "ctx._source.invoice_id = ctx._source.remove('legacy_invoice_id')"
          }
        }

        POST _aliases
        {
          "actions": [
            { "remove": { "index": "orders-v1", "alias": "orders" } },
            { "add":    { "index": "orders-v2", "alias": "orders", "is_write_index": true } }
          ]
        }
        ```

        ### 3.4. ILM-политика

        ```json
        PUT _ilm/policy/orders-policy
        {
          "policy": {
            "phases": {
              "hot":    { "actions": { "rollover": { "max_primary_shard_size": "50gb" } } },
              "warm":   { "min_age": "7d",  "actions": { "forcemerge": { "max_num_segments": 1 } } },
              "cold":   { "min_age": "30d", "actions": { "freeze": {} } },
              "delete": { "min_age": "180d", "actions": { "delete": {} } }
            }
          }
        }
        ```

        ---

        ## 4. Порядок выполнения

        | Шаг | Команда | Окно | Откат |
        |---:|---|---|---|
        | 1 | `pg_dump --schema-only -Fc` | 00:00–00:05 | восстановить дамп |
        | 2 | Применить DDL PostgreSQL | 00:05–00:15 | `migrate down 2026.11.26-01` |
        | 3 | Создать процедуры и триггеры | 00:15–00:20 | удалить объекты |
        | 4 | Создать индексный шаблон ES | 00:20–00:25 | удалить шаблон |
        | 5 | Запустить reindex `orders-v1 → orders-v2` | 00:25–00:45 | вернуть alias на `orders-v1` |
        | 6 | Smoke-тесты и сверка счётчиков | 00:45–00:55 | переключить alias обратно |

        ## 5. Проверки после миграции

        - [ ] `select count(*) from billing.invoice` совпадает с ожидаемым.
        - [ ] `select count(*) from billing.invoice where total_amount < 0` возвращает `0`.
        - [ ] Все внешние ключи в состоянии `convalidated`.
        - [ ] `GET orders/_mapping` содержит `invoice_id` и `items.amount`.
        - [ ] `GET _cat/aliases/orders?v` показывает `orders-v2` как write index.
        - [ ] `GET _cluster/health?wait_for_status=yellow` возвращает `yellow` или `green`.
        - [ ] Ошибки в логах приложения за окно миграции отсутствуют.

        ## 6. Риски и откат

        | Риск | Вероятность | Митигация |
        |---|---|---|
        | Долгая блокировка `public.client` | средняя | `CREATE INDEX CONCURRENTLY`, короткий DDL |
        | Reindex дольше окна | средняя | `wait_for_completion=false`, статус через `_tasks` |
        | Потеря данных при backfill | низкая | идемпотентные батчи + сверка count/sum |
        | Несовместимый mapping | низкая | `dynamic: strict` и тестовый индекс `orders-v2-preview` |

        ```sql
        -- Откат PostgreSQL
        DROP TRIGGER IF EXISTS tr_invoice_touch ON billing.invoice;
        DROP VIEW IF EXISTS billing.v_invoice_summary;
        DROP PROCEDURE IF EXISTS billing.close_period(date);
        DROP FUNCTION IF EXISTS billing.recalc_invoice(bigint);
        DROP TABLE IF EXISTS billing.payment;
        DROP TABLE IF EXISTS billing.invoice_line;
        DROP TABLE IF EXISTS billing.invoice;
        ```

        ```json
        // Откат Elasticsearch: вернуть alias на старый индекс
        POST _aliases
        {
          "actions": [
            { "remove": { "index": "orders-v2", "alias": "orders" } },
            { "add":    { "index": "orders-v1", "alias": "orders", "is_write_index": true } }
          ]
        }
        ```

        ## 7. Источники

        - [PostgreSQL 16 — CREATE TABLE](https://www.postgresql.org/docs/16/sql-createtable.html)
        - [PostgreSQL 16 — CREATE PROCEDURE](https://www.postgresql.org/docs/16/sql-createprocedure.html)
        - [PostgreSQL 16 — CREATE INDEX](https://www.postgresql.org/docs/16/sql-createindex.html)
        - [Elasticsearch 8.15 — PUT mapping](https://www.elastic.co/guide/en/elasticsearch/reference/8.15/indices-put-mapping.html)
        - [Elasticsearch 8.15 — Index templates](https://www.elastic.co/guide/en/elasticsearch/reference/8.15/index-templates.html)
        - [Elasticsearch 8.15 — Reindex API](https://www.elastic.co/guide/en/elasticsearch/reference/8.15/docs-reindex.html)

        ---

        *Это демонстрационный отчёт-заглушка. Команды приведены как пример и не
        выполняются приложением.*
        """;
}
