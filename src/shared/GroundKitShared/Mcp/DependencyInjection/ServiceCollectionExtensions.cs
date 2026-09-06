using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace GroundKit.Mcp.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGroundKitMcp(this IServiceCollection services)
    {
        services.AddSingleton<GroundKitToolResponseAgent>();
        services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithHttpTransport()
            .WithToolsFromAssembly();
        services.AddSingleton<GroundKitMcpServer>();
        return services;
    }
}
