using Dashboard.Client;

namespace App2.Counter;

/// <summary>
/// Example panel owned by app2. Applying it changes how the counter ticks.
/// </summary>
[DashboardPanel("Панель app2", order: 20)]
public sealed class App2DemoPanel(CounterService counter, ILogger<App2DemoPanel> logger)
{
    [DashboardInput("Шаг увеличения", DashboardInputType.Int, sortOrder: 1)]
    public int Step { get; set; }

    [DashboardInput("Счётчик включён", DashboardInputType.Bool, sortOrder: 2)]
    public bool Enabled { get; set; }

    public Task Render()
    {
        Step = counter.Step;
        Enabled = counter.Enabled;
        return Task.CompletedTask;
    }

    [DashboardAction("Применить", sortOrder: 1)]
    public Task<string> Apply()
    {

        Step++;

        counter.SetStep(Step);
        counter.SetEnabled(Enabled);

        logger.LogInformation(
            "PANEL APPLIED panel=app2 step={Step} enabled={Enabled}",
            counter.Step, counter.Enabled);

        return Task.FromResult(
            $"Счётчик: шаг={counter.Step}, включён={counter.Enabled}");
    }
}
