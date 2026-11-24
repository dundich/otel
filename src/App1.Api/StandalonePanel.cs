using Dashboard.Client;

namespace App1.Api;

/// <summary>
/// Extra example panel that is registered only in the standalone version
/// (Standalone:Enabled=true). It shows all supported input types:
/// text, date, int and bool.
/// </summary>
[DashboardPanel("Панель standalone · app1", order: 15, Description = "Демо всех типов полей")]
public sealed class StandalonePanel(ILogger<StandalonePanel> logger)
{
    [DashboardInput("Комментарий", DashboardInputType.Text, sortOrder: 1)]
    public string Comment { get; set; } = string.Empty;

    [DashboardInput("Дата запуска", DashboardInputType.Date, sortOrder: 2)]
    public DateOnly RunDate { get; set; }

    [DashboardInput("Повторы", DashboardInputType.Int, sortOrder: 3)]
    public int Retries { get; set; }

    [DashboardInput("Только проверка", DashboardInputType.Bool, sortOrder: 4)]
    public bool DryRun { get; set; }

    public Task Render()
    {
        Comment = "standalone demo";
        RunDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        Retries = 3;
        DryRun = true;
        return Task.CompletedTask;
    }

    [DashboardAction("Применить", sortOrder: 1)]
    public Task<string> Apply()
    {
        logger.LogInformation(
            "STANDALONE PANEL APPLIED comment={Comment} runDate={RunDate} retries={Retries} dryRun={DryRun}",
            Comment, RunDate, Retries, DryRun);

        return Task.FromResult(
            $"Standalone: комментарий={Comment}, дата={RunDate:yyyy-MM-dd}, повторы={Retries}, проверка={DryRun}");
    }
}
