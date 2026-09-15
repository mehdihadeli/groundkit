using GroundKit.Cli;
using GroundKitShared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Error);
builder.Services.AddGroundKitServices();
builder.Services.AddSingleton<CliApplication>();

var app = CliCommandAppFactory.Create(builder.Services);
return await app.RunAsync(args);
