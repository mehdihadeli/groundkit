using Microsoft.Extensions.DependencyInjection;

namespace DocsContext.Mcp.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDocsContextMcp(this IServiceCollection services)
    {
        services.AddSingleton<DocsContextToolResponseAgent>();
        services.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly();
        services.AddSingleton<DocsContextMcpServer>();
        return services;
    }
}
