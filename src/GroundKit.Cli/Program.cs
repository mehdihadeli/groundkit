using GroundKit.Cli;
using GroundKit.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var verbose = args.Any(argument =>
    string.Equals(argument, "--verbose", StringComparison.OrdinalIgnoreCase)
);
args = args.Where(argument =>
        !string.Equals(argument, "--verbose", StringComparison.OrdinalIgnoreCase)
    )
    .ToArray();

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(verbose ? LogLevel.Information : LogLevel.Error);
builder.Services.AddGroundKitServices();
builder.Services.AddSingleton<CliApplication>();

var app = CliCommandAppFactory.Create(builder.Services);
return await app.RunAsync(args);
