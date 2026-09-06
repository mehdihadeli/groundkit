using GroundKit.Core.Abstractions;
using GroundKit.Core.Contracts;
using GroundKit.Storage.Sqlite;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace GroundKit.Registry;

public sealed class RegistryApplication(
    IDocumentPackageBuilder packageBuilder,
    ILoggerFactory loggerFactory,
    RegistryPublisher publisher
)
{
    private readonly RegistryBundleService bundleService = new();

    public async Task<int> RunAsync(string[] args)
    {
        try
        {
            return NormalizeCommand(args.FirstOrDefault()) switch
            {
                "list" => List(args),
                "validate" => Validate(args),
                "build" => await BuildAsync(args),
                "build-all" => await BuildAllAsync(args, false),
                "publish" => await PublishAsync(args),
                "publish-all" => await BuildAllAsync(args, true),
                "bundle" => await BundleAsync(args),
                "import-bundle" => await ImportBundleAsync(args),
                _ => ShowHelp(),
            };
        }
        catch (Exception exception)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(exception.Message)}");
            return 1;
        }
    }

    private static int List(string[] args)
    {
        foreach (var definition in Load(args))
        {
            AnsiConsole.MarkupLine(
                $"[aqua]{definition.Registry}/{definition.Name}[/] "
                    + $"({(definition.IsVersioned ? "versioned" : "latest")}) {Markup.Escape(definition.Source)}"
            );
        }
        return 0;
    }

    private static string? NormalizeCommand(string? command) =>
        command?.ToLowerInvariant() switch
        {
            "--list" => "list",
            "--validate" => "validate",
            "--build" => "build",
            "--build-all" => "build-all",
            "--publish" => "publish",
            "--publish-all" => "publish-all",
            "--bundle" => "bundle",
            "--import-bundle" => "import-bundle",
            _ => command?.ToLowerInvariant(),
        };

    private static int Validate(string[] args)
    {
        var definitions = Load(args);
        AnsiConsole.MarkupLine($"[green]Validated {definitions.Count} registry definition(s).[/]");
        return 0;
    }

    private async Task<int> BuildAsync(string[] args)
    {
        if (args.Length < 2)
        {
            AnsiConsole.MarkupLine(
                "[red]Usage:[/] build <name> [version] [--dir <path>] [--output <path>]"
            );
            return 1;
        }

        var definition = Find(Load(args), args[1]);
        var selected = SelectVersion(definition, args);
        var path = await BuildDefinitionAsync(
            definition,
            Option(args, "--output") ?? "./dist-packages",
            selected
        );
        AnsiConsole.MarkupLine($"[green]Built:[/] {Markup.Escape(path)}");
        return 0;
    }

    private async Task<int> PublishAsync(string[] args)
    {
        if (args.Length < 2)
        {
            AnsiConsole.MarkupLine(
                "[red]Usage:[/] publish <name> [version] [--dir <path>] [--output <path>]"
            );
            return 1;
        }

        var definition = Find(Load(args), args[1]);
        var selected = SelectVersion(definition, args);
        if (await publisher.ExistsAsync(definition.Registry, definition.Name, selected.Version))
        {
            AnsiConsole.MarkupLine(
                $"Skipped existing: {definition.Registry}/{definition.Name}@{selected.Version}"
            );
            return 0;
        }

        var path = await BuildDefinitionAsync(
            definition,
            Option(args, "--output") ?? "./dist-packages",
            selected
        );
        await publisher.PublishAsync(definition.Registry, definition.Name, selected.Version, path);
        AnsiConsole.MarkupLine(
            $"[green]Published:[/] {definition.Registry}/{definition.Name}@{selected.Version}"
        );
        return 0;
    }

    private async Task<int> BuildAllAsync(string[] args, bool publish)
    {
        var definitions = Load(args);
        var output = Option(args, "--output") ?? "./dist-packages";
        Directory.CreateDirectory(output);
        var failures = 0;
        var succeeded = 0;

        foreach (var definition in definitions)
        foreach (var selected in definition.ResolveVersions())
        {
            try
            {
                if (
                    publish
                    && await publisher.ExistsAsync(
                        definition.Registry,
                        definition.Name,
                        selected.Version
                    )
                )
                {
                    AnsiConsole.MarkupLine(
                        $"Skipped existing: {definition.Registry}/{definition.Name}@{selected.Version}"
                    );
                    continue;
                }

                var path = await BuildDefinitionAsync(definition, output, selected);
                if (publish)
                {
                    await publisher.PublishAsync(
                        definition.Registry,
                        definition.Name,
                        selected.Version,
                        path
                    );
                }
                succeeded++;
                AnsiConsole.MarkupLine(
                    $"[green]{(publish ? "Published" : "Built")}:[/] {definition.Registry}/{definition.Name}@{selected.Version}"
                );
            }
            catch (Exception exception)
            {
                failures++;
                AnsiConsole.MarkupLine(
                    $"[red]Failed {definition.Registry}/{definition.Name}@{selected.Version}:[/] {Markup.Escape(exception.Message)}"
                );
            }
        }

        AnsiConsole.MarkupLine($"Summary: {succeeded} succeeded, {failures} failed.");
        return failures == 0 ? 0 : 1;
    }

    private async Task<int> BundleAsync(string[] args)
    {
        var output = Option(args, "--output") ?? "./dist-packages";
        var format = Option(args, "--format")?.ToLowerInvariant() ?? "zip";
        var extension = format switch
        {
            "zip" => ".zip",
            "tar.gz" or "tgz" => ".tar.gz",
            _ => throw new InvalidOperationException("Bundle format must be zip or tar.gz."),
        };
        var destination =
            Option(args, "--destination") ?? Path.Combine(output, "groundkit-registry" + extension);
        var path = await bundleService.CreateAsync(output, destination);
        AnsiConsole.MarkupLine($"[green]Bundle created:[/] {Markup.Escape(path)}");
        return 0;
    }

    private async Task<int> ImportBundleAsync(string[] args)
    {
        if (args.Length < 2)
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] import-bundle <path> [--output <path>]");
            return 1;
        }
        var output = Option(args, "--output") ?? "./dist-packages";
        var paths = await bundleService.ImportAsync(args[1], output);
        AnsiConsole.MarkupLine($"[green]Imported {paths.Count} package(s).[/]");
        return 0;
    }

    private async Task<string> BuildDefinitionAsync(
        RegistryDefinition definition,
        string output,
        (string Version, string? Tag) selected
    )
    {
        if (definition.Kind == SourceKind.LocalDirectory && !Directory.Exists(definition.Source))
        {
            throw new DirectoryNotFoundException(
                $"Source directory was not found: {definition.Source}"
            );
        }

        var result = await packageBuilder.BuildAsync(
            definition.Source,
            definition.DocsPath,
            default,
            selected.Version,
            selected.Tag
        );
        var displayName = string.IsNullOrWhiteSpace(definition.Description)
            ? definition.Name
            : definition.Description;
        var normalized = result with
        {
            Source = result.Source with
            {
                CanonicalId = definition.Name,
                DisplayName = displayName,
            },
            Manifest = result.Manifest with
            {
                PackageId = definition.Name,
                DisplayName = displayName,
                Version = selected.Version,
                SourceCanonicalId = definition.Name,
            },
        };
        var store = new SqlitePackageStore(
            new PackageStoreOptions(Path.GetFullPath(output)),
            loggerFactory.CreateLogger<SqlitePackageStore>()
        );
        return await store.SaveAsync(normalized);
    }

    private static (string Version, string? Tag) SelectVersion(
        RegistryDefinition definition,
        string[] args
    )
    {
        var requested =
            args.Length > 2 && !args[2].StartsWith("--", StringComparison.Ordinal) ? args[2] : null;
        var versions = definition.ResolveVersions();
        var selected = versions.SingleOrDefault(item =>
            item.Version == (requested ?? versions[0].Version)
        );
        return selected == default
            ? throw new InvalidOperationException(
                $"Version '{requested}' is not defined for {definition.Name}."
            )
            : selected;
    }

    private static RegistryDefinition Find(
        IReadOnlyList<RegistryDefinition> definitions,
        string name
    ) =>
        definitions.SingleOrDefault(item =>
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                $"{item.Registry}/{item.Name}",
                name,
                StringComparison.OrdinalIgnoreCase
            )
        ) ?? throw new InvalidOperationException($"Definition '{name}' was not found.");

    private static IReadOnlyList<RegistryDefinition> Load(string[] args)
    {
        var definitions = RegistryDefinitionLoader.LoadDirectory(
            Option(args, "--dir") ?? "registry"
        );
        RegistryDefinitionLoader.Validate(definitions);
        return definitions;
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.FindIndex(
            args,
            arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase)
        );
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static int ShowHelp()
    {
        AnsiConsole.MarkupLine(
            "groundkit-registry list|validate|build|build-all|publish|publish-all|bundle|import-bundle [--dir <path>] [--output <path>]"
        );
        return 0;
    }
}
