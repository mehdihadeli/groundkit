using GroundKit.Hosting;
using GroundKit.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.AddServiceDefaults();
builder.Services.AddGroundKitServices();
builder.Services.AddSingleton<RegistryApplication>();
builder.Services.AddHttpClient<RegistryPublisher>(client =>
{
    var baseUrl =
        Environment.GetEnvironmentVariable("REGISTRY_SERVER_URL")?.TrimEnd('/')
        ?? "http://localhost:8080";
    client.BaseAddress = new Uri(baseUrl + "/");
});

var app = RegistryCommandAppFactory.Create(builder.Services);
return await app.RunAsync(args);
