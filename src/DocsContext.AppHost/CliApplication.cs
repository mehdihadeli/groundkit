using DocsContext.Core.Abstractions;
using DocsContext.Core.Contracts;
using DocsContext.Mcp;
using Spectre.Console;

namespace DocsContext.AppHost;

public sealed class CliApplication(
    IDocumentPackageBuilder packageBuilder,
    IPackageStore packageStore,
    DocsContextMcpServer mcpServer,
    IContextRegistryClient? registryClient = null
)
{
    public async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            ShowHelp();
            return 0;
        }

        var command = args[0].ToLowerInvariant();

        try
        {
            return command switch
            {
                "add" => await RunAddAsync(args),
                "import" => await RunImportAsync(args),
                "export" => await RunExportAsync(args),
                "list" => await RunListAsync(),
                "inspect" => await RunInspectAsync(args),
                "query" => await RunQueryAsync(args),
                "refresh" => await RunRefreshAsync(args),
                "remove" => await RunRemoveAsync(args),
                "serve" => await RunServeAsync(),
                "search-packages" => await RunSearchPackagesAsync(args),
                "download-package" => await RunDownloadPackageAsync(args),
                _ => ShowUnknownCommand(command),
            };
        }
        catch (Exception exception)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(exception.Message)}");
            return 1;
        }
    }

    private async Task<int> RunAddAsync(string[] args)
    {
        if (args.Length < 2)
        {
            AnsiConsole.Markup("[red]Usage:[/] ");
            AnsiConsole.WriteLine("add <source> [--docs-path <path>]");
            return 1;
        }

        var catalogEntry = LibraryCatalog.Find(args[1]);
        var source = catalogEntry?.Repository ?? args[1];
        var docsPath = TryReadOption(args, "--docs-path") ?? catalogEntry?.DocsPath;

        var buildResult = await packageBuilder.BuildAsync(source, docsPath);
        var packagePath = await packageStore.SaveAsync(buildResult);

        AnsiConsole.MarkupLine(
            $"[green]Added package:[/] {Markup.Escape(buildResult.Manifest.PackageId)}"
        );
        AnsiConsole.MarkupLine($"Path: {Markup.Escape(packagePath)}");
        AnsiConsole.MarkupLine(
            $"Documents: {buildResult.Manifest.DocumentCount}, Chunks: {buildResult.Manifest.ChunkCount}"
        );

        if (buildResult.Warnings.Count > 0)
        {
            var warningTable = new Table().AddColumn("Warning").AddColumn("Source");
            foreach (var warning in buildResult.Warnings)
            {
                warningTable.AddRow(warning.Message, warning.SourcePath ?? "-");
            }

            AnsiConsole.Write(warningTable);
        }

        return 0;
    }

    private async Task<int> RunListAsync()
    {
        var packages = await packageStore.ListAsync();
        if (packages.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No packages installed yet.[/]");
            return 0;
        }

        var table = new Table();
        table.AddColumn("Package");
        table.AddColumn("Version");
        table.AddColumn("Documents");
        table.AddColumn("Chunks");
        table.AddColumn("Built At");

        foreach (var package in packages)
        {
            table.AddRow(
                package.PackageId,
                package.Version ?? "dev",
                package.DocumentCount.ToString(),
                package.ChunkCount.ToString(),
                package.BuiltAt.ToString("u")
            );
        }

        AnsiConsole.Write(table);
        return 0;
    }

    private async Task<int> RunImportAsync(string[] args)
    {
        if (args.Length < 2)
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] import <package-file>");
            return 1;
        }

        var importedPath = await packageStore.ImportAsync(args[1]);
        AnsiConsole.MarkupLine($"[green]Imported package:[/] {Markup.Escape(importedPath)}");
        return 0;
    }

    private async Task<int> RunExportAsync(string[] args)
    {
        if (args.Length < 3)
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] export <package-id> <destination>");
            return 1;
        }

        var exportPath = await packageStore.ExportAsync(args[1], args[2]);
        AnsiConsole.MarkupLine($"[green]Exported package:[/] {Markup.Escape(exportPath)}");
        return 0;
    }

    private async Task<int> RunInspectAsync(string[] args)
    {
        if (args.Length < 2)
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] inspect <package-id>");
            return 1;
        }

        var package = await packageStore.GetPackageAsync(args[1]);
        if (package is null)
        {
            AnsiConsole.MarkupLine($"[yellow]Package not found:[/] {Markup.Escape(args[1])}");
            return 1;
        }

        var source = await packageStore.GetSourceAsync(args[1]);
        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddRow("Package", package.PackageId);
        grid.AddRow("Display name", package.DisplayName);
        grid.AddRow("Version", package.Version ?? "dev");
        grid.AddRow("Documents", package.DocumentCount.ToString());
        grid.AddRow("Chunks", package.ChunkCount.ToString());
        grid.AddRow("Built at", package.BuiltAt.ToString("u"));
        grid.AddRow("Path", package.PackagePath);

        if (source is not null)
        {
            grid.AddRow("Source kind", source.Kind.ToString());
            grid.AddRow("Source location", source.Location);
            grid.AddRow("Docs path", source.DocsPath ?? "-");
            grid.AddRow("Source version", source.Version ?? "-");
            grid.AddRow("Tag", source.Tag ?? "-");
            grid.AddRow("Branch", source.Branch ?? "-");
            grid.AddRow("Fingerprint", source.Fingerprint ?? "-");
        }

        AnsiConsole.Write(new Panel(grid).Header($"Package {package.PackageId}").Expand());
        return 0;
    }

    private async Task<int> RunQueryAsync(string[] args)
    {
        if (args.Length < 3)
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] query <package-id> <topic>");
            return 1;
        }

        var packageId = args[1];
        var topic = string.Join(' ', args.Skip(2));
        var response = await packageStore.QueryAsync(new DocsQueryRequest(packageId, topic));

        if (response.Hits.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No matching documentation found.[/]");
            return 0;
        }

        foreach (var hit in response.Hits)
        {
            AnsiConsole.Write(
                new Panel(hit.Content).Header($"{hit.DocumentTitle} / {hit.SectionTitle}").Expand()
            );
            AnsiConsole.MarkupLine(
                $"Tokens: {hit.TokenEstimate} | Score: {hit.Score:F3} | HasCode: {hit.HasCode}"
            );
            AnsiConsole.WriteLine();
        }

        AnsiConsole.MarkupLine($"[green]Total tokens returned:[/] {response.TotalTokens}");
        return 0;
    }

    private async Task<int> RunRefreshAsync(string[] args)
    {
        if (args.Length < 2)
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] refresh <package-id>");
            return 1;
        }

        var source = await packageStore.GetSourceAsync(args[1]);
        if (source is null)
        {
            AnsiConsole.MarkupLine($"[red]Package not found:[/] {Markup.Escape(args[1])}");
            return 1;
        }

        var buildResult = await packageBuilder.BuildAsync(source.Location, source.DocsPath);
        var packagePath = await packageStore.SaveAsync(buildResult);

        AnsiConsole.MarkupLine(
            $"[green]Refreshed package:[/] {Markup.Escape(buildResult.Manifest.PackageId)}"
        );
        AnsiConsole.MarkupLine($"Path: {Markup.Escape(packagePath)}");
        AnsiConsole.MarkupLine(
            $"Documents: {buildResult.Manifest.DocumentCount}, Chunks: {buildResult.Manifest.ChunkCount}"
        );
        return 0;
    }

    private async Task<int> RunRemoveAsync(string[] args)
    {
        if (args.Length < 2)
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] remove <package-id>");
            return 1;
        }

        var removedCount = await packageStore.RemoveAsync(args[1]);
        if (removedCount == 0)
        {
            AnsiConsole.MarkupLine($"[yellow]Package not found:[/] {Markup.Escape(args[1])}");
            return 1;
        }

        AnsiConsole.MarkupLine(
            $"[green]Removed package files:[/] {Markup.Escape(args[1])} ({removedCount})"
        );
        return 0;
    }

    private async Task<int> RunServeAsync()
    {
        await mcpServer.RunAsync();
        return 0;
    }

    private async Task<int> RunSearchPackagesAsync(string[] args)
    {
        if (registryClient is null)
        {
            AnsiConsole.MarkupLine("[red]Registry client is unavailable.[/]");
            return 1;
        }

        if (args.Length < 3)
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] search-packages <registry> <name> [version]");
            return 1;
        }

        var results = await registryClient.SearchAsync(
            args[1],
            args[2],
            args.Length > 3 ? args[3] : null
        );
        if (results.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No packages found.[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Package");
        table.AddColumn("Version");
        table.AddColumn("Description");
        table.AddColumn("Size");
        foreach (var result in results)
        {
            table.AddRow(
                Markup.Escape(result.Name),
                Markup.Escape(result.Version),
                Markup.Escape(result.Description ?? "-"),
                result.Size is long size ? $"{size:N0} B" : "-"
            );
        }

        AnsiConsole.Write(table);
        return 0;
    }

    private async Task<int> RunDownloadPackageAsync(string[] args)
    {
        if (registryClient is null)
        {
            AnsiConsole.MarkupLine("[red]Registry client is unavailable.[/]");
            return 1;
        }

        if (args.Length < 4)
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] download-package <registry> <name> <version>");
            return 1;
        }

        var temporaryPath = Path.Combine(Path.GetTempPath(), $"docs-context-{Guid.NewGuid():N}.db");
        try
        {
            await registryClient.DownloadAsync(args[1], args[2], args[3], temporaryPath);
            var installedPath = await packageStore.ImportAsync(temporaryPath);
            AnsiConsole.MarkupLine($"[green]Installed package:[/] {Markup.Escape(installedPath)}");
            return 0;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static int ShowUnknownCommand(string command)
    {
        AnsiConsole.MarkupLine($"[red]Unknown command:[/] {Markup.Escape(command)}");
        ShowHelp();
        return 1;
    }

    private static void ShowHelp()
    {
        AnsiConsole.MarkupLine("[bold]DocsContext[/]");
        AnsiConsole.MarkupLine("Commands:");
        AnsiConsole.WriteLine("  add <source> [--docs-path path]");
        AnsiConsole.MarkupLine("  import <package-file>");
        AnsiConsole.MarkupLine("  export <package-id> <destination>");
        AnsiConsole.MarkupLine("  list");
        AnsiConsole.MarkupLine("  inspect <package-id>");
        AnsiConsole.MarkupLine("  query <package-id> <topic>");
        AnsiConsole.MarkupLine("  refresh <package-id>");
        AnsiConsole.MarkupLine("  remove <package-id>");
        AnsiConsole.MarkupLine("  serve");
        AnsiConsole.WriteLine("  catalog [query]");
        AnsiConsole.WriteLine("  search-packages <registry> <name> [version]");
        AnsiConsole.WriteLine("  download-package <registry> <name> <version>");
    }

    private static string? TryReadOption(IReadOnlyList<string> args, string optionName)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], optionName, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}
