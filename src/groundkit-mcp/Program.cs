using GroundKit.Mcp;
using GroundKit.Mcp.DependencyInjection;
using GroundKitShared;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var libraries = Array.FindIndex(
    args,
    argument => string.Equals(argument, "--libs", StringComparison.OrdinalIgnoreCase)
);
if (libraries >= 0 && libraries + 1 < args.Length)
{
    Environment.SetEnvironmentVariable("GROUNDKIT_ALLOWED_LIBRARIES", args[libraries + 1]);
}

if (args.Length > 0 && string.Equals(args[0], "http", StringComparison.OrdinalIgnoreCase))
{
    var webBuilder = WebApplication.CreateBuilder(args);
    webBuilder.AddServiceDefaults();
    webBuilder.Services.AddGroundKitServices();
    webBuilder.Services.AddGroundKitMcp();

    var webApp = webBuilder.Build();
    webApp.MapDefaultEndpoints();
    webApp.MapMcp("/mcp");
    await webApp.RunAsync();
    return 0;
}

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddGroundKitServices();
builder.Services.AddGroundKitMcp();

using var host = builder.Build();
await host.Services.GetRequiredService<GroundKitMcpServer>().RunAsync();
return 0;
