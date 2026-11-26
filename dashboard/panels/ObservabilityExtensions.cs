using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace RaMonitor.Panels;

/// <summary>
/// Minimal OpenTelemetry setup without Aspire. Telemetry is exported over OTLP/gRPC
/// to the standalone Aspire Dashboard container (or any other OTLP endpoint).
/// </summary>
public static class ObservabilityExtensions
{
    public static TBuilder AddDemoObservability<TBuilder>(
        this TBuilder builder,
        string serviceName,
        params string[] meters)
        where TBuilder : IHostApplicationBuilder
    {
        // Standard OTel environment variable, e.g. http://aspire-dashboard:18889
        var endpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
                       ?? "http://localhost:4317";

        var resource = ResourceBuilder.CreateDefault()
            // Стабильный идентификатор: без авто-генерации service.instance.id
            // перезапуски процесса схлопываются в один ресурс дашборда,
            // а не копят app1-xxxxxxxx при каждом рестарте.
            // При необходимости instance id можно задать явно через
            // OTEL_RESOURCE_ATTRIBUTES=service.instance.id=...
            .AddService(serviceName, serviceVersion: "1.0.0", autoGenerateServiceInstanceId: false)
            .AddAttributes(new Dictionary<string, object>
            {
                ["deployment.environment.name"] = builder.Environment.EnvironmentName
            });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.SetResourceBuilder(resource)
                    .AddMeter(meters)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter(options => ConfigureOtlp(options, endpoint));
            })
            .WithTracing(tracing =>
            {
                tracing.SetResourceBuilder(resource)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter(options => ConfigureOtlp(options, endpoint));
            });

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.SetResourceBuilder(resource);
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
            logging.AddOtlpExporter(options => ConfigureOtlp(options, endpoint));
        });

        return builder;
    }

    private static void ConfigureOtlp(OtlpExporterOptions options, string endpoint)
    {
        options.Endpoint = new Uri(endpoint);
        options.Protocol = OtlpExportProtocol.Grpc;
    }
}
