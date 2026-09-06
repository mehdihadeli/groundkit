using GroundKit.Configuration;
using GroundKit.Ingestion.DependencyInjection;
using GroundKit.Mcp.DependencyInjection;
using GroundKit.Storage.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace GroundKitShared;

public static class GroundKitServiceCollectionExtensions
{
    public static IServiceCollection AddGroundKitServices(this IServiceCollection services)
    {
        services.AddSingleton(GroundKitOptions.Load());
        return services.AddGroundKitIngestion().AddGroundKitStorage().AddGroundKitMcp();
    }
}
