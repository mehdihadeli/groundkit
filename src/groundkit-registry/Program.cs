using GroundKit.Registry;
using GroundKitShared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

if (args.FirstOrDefault()?.Equals("serve", StringComparison.OrdinalIgnoreCase) == true)
{
    return await RegistryServer.RunAsync(args.Skip(1).ToArray());
}

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

using var host = builder.Build();
return await host.Services.GetRequiredService<RegistryApplication>().RunAsync(args);
