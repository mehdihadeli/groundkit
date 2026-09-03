using DocsContext.Core.Abstractions;
using DocsContext.Core.Contracts;
using DocsContext.Storage.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace DocsContext.Storage.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDocsContextStorage(this IServiceCollection services)
    {
        var configuredRoot = Environment.GetEnvironmentVariable("DOCSCONTEXT_HOME");
        var rootPath = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(Environment.CurrentDirectory, ".docs-context", "packages")
            : Path.Combine(configuredRoot, "packages");

        services.AddSingleton(new PackageStoreOptions(rootPath));
        services.AddSingleton<IPackageStore, SqlitePackageStore>();

        return services;
    }
}
