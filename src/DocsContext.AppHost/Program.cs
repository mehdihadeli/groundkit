using DocsContext.AppHost;
using DocsContext.Ingestion.DependencyInjection;
using DocsContext.Mcp.DependencyInjection;
using DocsContext.Observability.Telemetry;
using DocsContext.Storage.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Spectre.Console.Cli;

var builder = Host.CreateApplicationBuilder(args);
var enableConsoleExporter = string.Equals(
    Environment.GetEnvironmentVariable("DOCSCONTEXT_OTEL_CONSOLE"),
    "1",
    StringComparison.Ordinal
);

builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder
    .Services.AddDocsContextIngestion()
    .AddDocsContextStorage()
    .AddDocsContextMcp()
    .AddSingleton<CliApplication>();

builder
    .Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddSource(DocsContextTelemetry.ActivitySourceName);
        if (enableConsoleExporter)
        {
            tracing.AddConsoleExporter();
        }
    })
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(DocsContextTelemetry.MeterName);
        if (enableConsoleExporter)
        {
            metrics.AddConsoleExporter();
        }
    });

using var host = builder.Build();
using var startupActivity = DocsContextTelemetry.ActivitySource.StartActivity("app-start");

if (args.Length > 0 && string.Equals(args[0], "catalog", StringComparison.OrdinalIgnoreCase))
{
    var catalogApp = new CommandApp();
    catalogApp.Configure(config =>
    {
        config.SetApplicationName("docs-context");
        config.AddCommand<CatalogCommand>("catalog");
    });
    return await catalogApp.RunAsync(args);
}

var application = host.Services.GetRequiredService<CliApplication>();
return await application.RunAsync(args);
