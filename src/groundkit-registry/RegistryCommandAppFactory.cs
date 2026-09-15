using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace GroundKit.Registry;

internal static class RegistryCommandAppFactory
{
    public static CommandApp Create(IServiceCollection services)
    {
        var app = new CommandApp(new ServiceCollectionTypeRegistrar(services));
        app.Configure(config =>
        {
            config.SetApplicationName("groundkit-registry");
            config
                .AddCommand<ListRegistryCommand>("list")
                .WithAlias("l")
                .WithAlias("ls")
                .WithDescription("List registry definitions from a directory.")
                .WithExample("ls", "-d", "registry");
            config
                .AddCommand<ValidateRegistryCommand>("validate")
                .WithAlias("v")
                .WithAlias("val")
                .WithDescription("Validate registry definition files.")
                .WithExample("v", "-d", "registry");
            config
                .AddCommand<BuildRegistryCommand>("build")
                .WithAlias("b")
                .WithDescription("Build one package from a registry definition.")
                .WithExample("b", "react", "-d", "registry", "-o", "./dist-packages");
            config
                .AddCommand<BuildAllRegistryCommand>("build-all")
                .WithAlias("ba")
                .WithDescription("Build all registry definitions into package artifacts.")
                .WithExample("ba", "-d", "registry", "-o", "./dist-packages");
            config
                .AddCommand<PublishRegistryCommand>("publish")
                .WithAlias("p")
                .WithAlias("pub")
                .WithDescription("Publish one built package to a compatible registry API.")
                .WithExample("p", "react", "-d", "registry", "-o", "./dist-packages");
            config
                .AddCommand<PublishAllRegistryCommand>("publish-all")
                .WithAlias("pa")
                .WithDescription("Publish all definitions and continue past individual failures.")
                .WithExample("pa", "-d", "registry", "-o", "./dist-packages");
            config
                .AddCommand<BundleRegistryCommand>("bundle")
                .WithAlias("bd")
                .WithAlias("bun")
                .WithDescription("Bundle built package artifacts into zip or tar.gz.")
                .WithExample("bd", "-o", "./dist-packages", "-f", "zip");
            config
                .AddCommand<ImportBundleRegistryCommand>("import-bundle")
                .WithAlias("ib")
                .WithDescription("Extract a registry bundle into local package artifacts.")
                .WithExample(
                    "ib",
                    "./dist-packages/groundkit-registry.zip",
                    "-o",
                    "./imported-packages"
                );
            config
                .AddCommand<ServeRegistryCommand>("serve")
                .WithAlias("s")
                .WithDescription("Start the local registry HTTP API.")
                .WithExample("s", "-u", "http://localhost:8080");
        });
        return app;
    }
}

internal sealed class ServiceCollectionTypeRegistrar(IServiceCollection services) : ITypeRegistrar
{
    public ITypeResolver Build() =>
        new ServiceCollectionTypeResolver(services.BuildServiceProvider());

    public void Register(Type service, Type implementation) =>
        services.AddSingleton(service, implementation);

    public void RegisterInstance(Type service, object implementation) =>
        services.AddSingleton(service, implementation);

    public void RegisterLazy(Type service, Func<object> factory) =>
        services.AddSingleton(service, _ => factory());
}

internal sealed class ServiceCollectionTypeResolver(ServiceProvider provider)
    : ITypeResolver,
        IDisposable
{
    public object? Resolve(Type? type) =>
        type is null
            ? null
            : provider.GetService(type) ?? ActivatorUtilities.CreateInstance(provider, type);

    public void Dispose() => provider.Dispose();
}
