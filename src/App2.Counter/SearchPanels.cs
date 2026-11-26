using System.Text;
using System.Text.Json;
using RaMonitor.Panels;

namespace App2.Counter;

/// <summary>
/// Base class for the demo search panels. Search panels are registered with
/// <c>SearchPanel&lt;T&gt;(...)</c> and appear on the «Поиск» tab. The dashboard
/// calls <c>ExecuteAsync</c> when a toolbar button is pressed and appends the
/// returned content to the result list.
/// </summary>
public abstract class DemoSearchPanel : IDashboardPanel
{
    public abstract Task<DashboardContent> Find();

    /// <summary>
    /// Search panels are singletons with query values bound to properties.
    /// Reset them before the descriptor is read and after an action, so every
    /// GET /dashboard/panels returns clean defaults instead of the last query.
    /// </summary>
    protected abstract void ResetValues();

    public Task RenderAsync(CancellationToken cancellationToken = default)
    {
        ResetValues();
        return Task.CompletedTask;
    }

    public async Task<object?> ExecuteAsync(string actionName, CancellationToken cancellationToken = default)
        => actionName switch
        {
            "Find" => await FindAndResetAsync().ConfigureAwait(false),
            _ => throw new InvalidOperationException(
                $"Action '{actionName}' is not handled by {GetType().Name}.")
        };

    private async Task<DashboardContent> FindAndResetAsync()
    {
        var content = await Find().ConfigureAwait(false);
        ResetValues();
        return content;
    }
}

/// <summary>Example filter: telephone calls by phone number.</summary>
public sealed class CallSearchPanel : DemoSearchPanel
{
    private static readonly (string Phone, string Client, int Minutes, string State)[] Calls =
    [
        ("+7 999 100-20-30", "ООО «Ромашка»", 12, "завершён"),
        ("+7 999 100-20-30", "ООО «Ромашка»", 4, "пропущен"),
        ("+7 999 555-66-77", "ИП Иванов", 31, "завершён"),
        ("+7 812 000-11-22", "ООО «Вектор»", 7, "завершён")
    ];

    public string Phone { get; set; } = string.Empty;

    protected override void ResetValues() => Phone = string.Empty;

    public override Task<DashboardContent> Find()
    {
        var matches = string.IsNullOrWhiteSpace(Phone)
            ? Calls
            : Calls.Where(call => call.Phone.Contains(Phone, StringComparison.OrdinalIgnoreCase)).ToArray();

        if (matches.Length == 0)
        {
            return Task.FromResult(DashboardContent.Text($"Звонки по номеру «{Phone}» не найдены."));
        }

        var markdown = new StringBuilder();
        markdown.AppendLine("## Результаты поиска звонков");
        markdown.AppendLine();
        markdown.AppendLine("| Телефон | Клиент | Минуты | Статус |");
        markdown.AppendLine("|---|---|---:|---|");

        foreach (var call in matches)
        {
            markdown.AppendLine($"| {call.Phone} | {call.Client} | {call.Minutes} | {call.State} |");
        }

        markdown.AppendLine();
        markdown.AppendLine($"Найдено: **{matches.Length}**. Запрос: `{Phone}`.");

        return Task.FromResult(DashboardContent.Markdown(markdown.ToString()));
    }
}

/// <summary>Example filter: import journal for a date range.</summary>
public sealed class ImportSearchPanel : DemoSearchPanel
{
    public DateOnly DateFrom { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(-7));

    public DateOnly DateTo { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    protected override void ResetValues()
    {
        DateFrom = DateOnly.FromDateTime(DateTime.Today.AddDays(-7));
        DateTo = DateOnly.FromDateTime(DateTime.Today);
    }

    public override Task<DashboardContent> Find()
    {
        var days = Math.Max(0, DateTo.DayNumber - DateFrom.DayNumber + 1);
        var payload = new
        {
            filter = new { dateFrom = DateFrom.ToString("yyyy-MM-dd"), dateTo = DateTo.ToString("yyyy-MM-dd") },
            periodDays = days,
            imported = days * 3,
            skipped = days / 2,
            errors = 0
        };

        return Task.FromResult(DashboardContent.Json(JsonSerializer.Serialize(
            payload,
            new JsonSerializerOptions { WriteIndented = true })));
    }
}

/// <summary>Example filter: invoice by number.</summary>
public sealed class InvoiceSearchPanel : DemoSearchPanel
{
    public string Number { get; set; } = string.Empty;

    protected override void ResetValues() => Number = string.Empty;

    public override Task<DashboardContent> Find()
    {
        var normalized = Number.Trim();

        if (normalized.Length == 0)
        {
            return Task.FromResult(DashboardContent.Text(
                "Введите номер накладной, например «НК-1042»."));
        }

        var markdown = $"""
            ## Накладная № {normalized}

            | Поле | Значение |
            |---|---|
            | Дата | {DateTime.Today:yyyy-MM-dd} |
            | Контрагент | ООО «Ромашка» |
            | Позиций | 12 |
            | Сумма | 148 500,00 ₽ |

            Статус: **отгружена**.
            """;

        return Task.FromResult(DashboardContent.Markdown(markdown));
    }
}
