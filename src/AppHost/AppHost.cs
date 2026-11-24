using System.Net.Http.Json;
using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

var app1Url = builder.Configuration["Demo:App1Url"] ?? "http://localhost:8081";
var app2Url = builder.Configuration["Demo:App2Url"] ?? "http://localhost:8082";

// The services themselves are started by docker compose. The AppHost is only a thin
// control shell: it shows them in the dashboard and adds a native dashboard command.
var app1 = builder.AddExternalService("app1", app1Url)
    .WithHttpHealthCheck("/healthz");

var app2 = builder.AddExternalService("app2", app2Url)
    .WithHttpHealthCheck("/healthz")
    .WithCommand(
        name: "send-counter",
        displayName: "Отправить счётчик в app1",
        executeCommand: async context =>
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            try
            {
                using var response = await client.PostAsJsonAsync(
                    $"{app2Url.TrimEnd('/')}/api/send",
                    new { },
                    context.CancellationToken);

                var body = await response.Content.ReadAsStringAsync(context.CancellationToken);

                return response.IsSuccessStatusCode
                    ? new ExecuteCommandResult { Success = true, Message = $"app1: {body}" }
                    : new ExecuteCommandResult { Success = false, Message = $"HTTP {(int)response.StatusCode}: {body}" };
            }
            catch (Exception ex)
            {
                return new ExecuteCommandResult { Success = false, Message = ex.Message };
            }
        },
        commandOptions: new CommandOptions
        {
            IconName = "Send",
            Description = "POST app2/api/send: отправляет текущее значение счётчика в app1."
        });

builder.Build().Run();
