using GroundKit.AppHost;
using GroundKitShared;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using Spectre.Console.Cli;

if (args.Length > 0 && string.Equals(args[0], "serve", StringComparison.OrdinalIgnoreCase))
{
    var libraries = Array.FindIndex(
        args,
        argument => string.Equals(argument, "--libs", StringComparison.OrdinalIgnoreCase)
    );
    if (libraries >= 0 && libraries + 1 < args.Length)
    {
        Environment.SetEnvironmentVariable("GROUNDKIT_ALLOWED_LIBRARIES", args[libraries + 1]);
    }
}

if (args.Length > 0 && string.Equals(args[0], "serve-http", StringComparison.OrdinalIgnoreCase))
{
    var webBuilder = WebApplication.CreateBuilder(args);
    webBuilder.Services.AddGroundKitServices();
    var webApp = webBuilder.Build();
    webApp.MapMcp("/mcp");
    await webApp.RunAsync();
    return 0;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
if (args.Length == 0 || !string.Equals(args[0], "serve", StringComparison.OrdinalIgnoreCase))
{
    builder.Logging.SetMinimumLevel(LogLevel.Error);
}
builder.AddServiceDefaults();
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
