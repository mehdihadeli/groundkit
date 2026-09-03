using DocsContext.Core.Abstractions;
using DocsContext.Ingestion.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DocsContext.Ingestion.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDocsContextIngestion(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddHttpClient<IContextRegistryClient, ContextRegistryClient>(client =>
        {
            client.BaseAddress = new Uri(ContextRegistryClient.ResolveBaseUrl() + "/");
            client.Timeout = TimeSpan.FromMinutes(5);
        });
        services.AddSingleton<ISourceDetector, SourceDetector>();
        services.AddSingleton<IDocumentPackageBuilder, DocumentPackageBuilder>();

        return services;
    }
}
