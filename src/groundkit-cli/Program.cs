using GroundKit.Cli;
using GroundKitShared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Error);
builder.Services.AddGroundKitServices();
builder.Services.AddSingleton<CliApplication>();

using var host = builder.Build();
if (
    args.Length > 0
    && (
        string.Equals(args[0], "catalog", StringComparison.OrdinalIgnoreCase)
        || string.Equals(args[0], "--catalog", StringComparison.OrdinalIgnoreCase)
    )
)
{
    var catalogArgs = args.ToArray();
    catalogArgs[0] = "catalog";
    var catalogApp = new CommandApp();
    catalogApp.Configure(config =>
    {
        config.SetApplicationName("groundkit");
        config.AddCommand<CatalogCommand>("catalog");
    });
    return await catalogApp.RunAsync(catalogArgs);
}

return await host.Services.GetRequiredService<CliApplication>().RunAsync(args);
