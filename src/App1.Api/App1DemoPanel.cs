using Dashboard.Client;

namespace App1.Api;

/// <summary>
/// Example panel owned by app1. The dashboard shows it in the accordion and
/// posts the input values back here when "Применить" is pressed.
/// </summary>
[DashboardPanel("Панель app1", order: 10)]
public sealed class App1DemoPanel(ILogger<App1DemoPanel> logger)
{
    [DashboardInput("Имя", sortOrder: 1)]
    public string Name { get; set; } = string.Empty;

    [DashboardInput("Дата рождения", DashboardInputType.Date, sortOrder: 2)]
    public DateOnly BirthDate { get; set; }

    [DashboardInput("Количество", DashboardInputType.Int, sortOrder: 3)]
    public int Count { get; set; }

    [DashboardInput("Активно", DashboardInputType.Bool, sortOrder: 4)]
    public bool Enabled { get; set; }

    // Render is optional. The SDK calls Render/RenderAsync before returning
    // the panel, so properties can be filled programmatically.
    public Task Render()
    {
        Name = "Ваня";
        BirthDate = new DateOnly(1990, 1, 1);
        Count = 1;
        Enabled = true;
        return Task.CompletedTask;
    }

    [DashboardAction("Применить", sortOrder: 1)]
    public Task<string> Apply()
    {
        logger.LogInformation(
            "PANEL APPLIED panel=app1 name={Name} birthDate={BirthDate} count={Count} enabled={Enabled}",
            Name, BirthDate, Count, Enabled);

        return Task.FromResult(
            $"Принято: имя={Name}, дата={BirthDate:yyyy-MM-dd}, количество={Count}, активно={Enabled}");
    }
}
