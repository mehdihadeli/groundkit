using System.Text.RegularExpressions;
using GroundKit.Core.Abstractions;
using GroundKit.Core.Contracts;
using GroundKit.Ingestion.Services;
using GroundKit.Mcp;
using Spectre.Console;

namespace GroundKit.AppHost;

public sealed class CliApplication(
    IDocumentPackageBuilder packageBuilder,
    IPackageStore packageStore,
    GroundKitMcpServer mcpServer,
    IContextRegistryClient? registryClient = null,
    IPackageDownloadService? packageDownloadService = null,
    IGitReferenceProvider? gitReferenceProvider = null
)
{
    public async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            ShowHelp();
            return 0;
        }

        var command = NormalizeCommand(args[0]);

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
                "serve" => await RunServeAsync(args),
                "serve-http" => ShowHttpServeHelp(),
                "search-packages" => await RunSearchPackagesAsync(args),
                "download-package" => await RunDownloadPackageAsync(args),
                "install" => await RunInstallAsync(args),
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
            AnsiConsole.WriteLine(
                "add <source> [--path <path>] [--name <name>] [--pkg-version <version>] [--save <path>] [--tag <tag>] [--choose-tag]"
            );
            return 1;
        }

        var catalogEntry = LibraryCatalog.Find(args[1]);
        var source = catalogEntry?.Repository ?? args[1];
        var docsPath =
            TryReadOption(args, "--path")
            ?? TryReadOption(args, "--docs-path")
            ?? catalogEntry?.DocsPath;
        var packageName = TryReadOption(args, "--name");
        var packageVersion = TryReadOption(args, "--pkg-version");
        var savePath = TryReadOption(args, "--save");
        var gitRef = TryReadOption(args, "--tag");
        var chooseTag = HasFlag(args, "--choose-tag");

        if (
            packageDownloadService is not null
            && (IsLocalPackageFile(source) || IsRemotePackageUrl(source))
        )
        {
            var importedPackagePath = await packageDownloadService.InstallAsync(
                source,
                packageVersion,
                CancellationToken.None
            );
            AnsiConsole.MarkupLine(
                $"[green]Added package:[/] {Markup.Escape(importedPackagePath)}"
            );
            return 0;
        }

        if (gitRef is null && IsGitRepositoryUrl(source) && gitReferenceProvider is not null)
        {
            var tags = await gitReferenceProvider.GetTagsAsync(source);
            if (chooseTag && tags.Count > 0 && AnsiConsole.Profile.Capabilities.Interactive)
            {
                var prompt = new SelectionPrompt<string>()
                    .Title("Select a [green]Git tag[/] or use the latest stable tag:")
                    .PageSize(12)
                    .AddChoices(["Use latest stable tag", .. tags]);
                var selection = AnsiConsole.Prompt(prompt);
                gitRef =
                    selection == "Use latest stable tag"
                        ? GitReferenceProvider.SelectLatestStableTag(tags)
                        : selection;
            }
            else
            {
                gitRef = GitReferenceProvider.SelectLatestStableTag(tags);
            }
        }

        var buildResult = await packageBuilder.BuildAsync(
            source,
            docsPath,
            CancellationToken.None,
            packageVersion,
            gitRef,
            packageName
        );
        var packagePath = await packageStore.SaveAsync(buildResult);
        var savedCopyPath = savePath is null
            ? null
            : await packageStore.ExportAsync(buildResult.Manifest.PackageId, savePath);

        AnsiConsole.MarkupLine(
            $"[green]Added package:[/] {Markup.Escape(buildResult.Manifest.PackageId)}"
        );
        AnsiConsole.MarkupLine($"Path: {Markup.Escape(packagePath)}");
        if (savedCopyPath is not null)
        {
            AnsiConsole.MarkupLine($"Saved copy: {Markup.Escape(savedCopyPath)}");
        }
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

        var table = new Table
        {
            Title = new TableTitle("Installed packages"),
            Border = TableBorder.Rounded,
        };
        table.AddColumn("Package");
        table.AddColumn("Version");
        table.AddColumn("Size");
        table.AddColumn("Documents");
        table.AddColumn("Chunks");

        foreach (var package in packages)
        {
            var size = File.Exists(package.PackagePath)
                ? FormatBytes(new FileInfo(package.PackagePath).Length)
                : "unknown size";
            table.AddRow(
                Markup.Escape(package.PackageId),
                Markup.Escape(package.Version ?? "dev"),
                size,
                $"{package.DocumentCount:N0}",
                $"{package.ChunkCount:N0}"
            );
        }

        AnsiConsole.Write(table);
        return 0;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes:N0} {units[unit]}" : $"{value:0.0} {units[unit]}";
    }

    private static bool IsGitRepositoryUrl(string input)
    {
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (input.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return uri.Host is "github.com" or "gitlab.com" or "bitbucket.org" or "codeberg.org"
            && uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).Length
                >= 2
            && !uri.AbsolutePath.Contains("/tree/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRemotePackageUrl(string input)
    {
        if (
            !Uri.TryCreate(input, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
        )
        {
            return false;
        }

        if (uri.AbsolutePath.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var fileName = uri.AbsolutePath.Trim('/').Split('/').LastOrDefault();
        return fileName is not null
            && Regex.IsMatch(
                fileName,
                @"^[^/]+@v?\d+(?:\.\d+){1,3}$",
                RegexOptions.CultureInvariant
            );
    }

    private static bool IsLocalPackageFile(string input) =>
        File.Exists(input) && input.EndsWith(".db", StringComparison.OrdinalIgnoreCase);

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
                new Panel(Markup.Escape(hit.Content))
                    .Header(Markup.Escape($"{hit.DocumentTitle} / {hit.SectionTitle}"))
                    .Expand()
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

    private async Task<int> RunServeAsync(string[] args)
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
        if (args.Length < 4)
        {
            AnsiConsole.MarkupLine("[red]Usage:[/] download-package <registry> <name> <version>");
            return 1;
        }

        if (packageDownloadService is null)
        {
            AnsiConsole.MarkupLine("[red]Package download service is unavailable.[/]");
            return 1;
        }

        var path = await packageDownloadService.InstallRegistryPackageAsync(
            args[1],
            args[2],
            args[3]
        );
        AnsiConsole.MarkupLine($"[green]Installed package:[/] {Markup.Escape(path)}");
        return 0;
    }

    private async Task<int> RunInstallAsync(string[] args)
    {
        if (args.Length < 2)
        {
            AnsiConsole.WriteLine("Usage: install <registry/name|name|source> [version]");
            return 1;
        }

        var requested = args[1];
        var requestedVersion = args.Length > 2 ? args[2] : null;

        if (packageDownloadService is null)
        {
            AnsiConsole.MarkupLine("[red]Package download service is unavailable.[/]");
            return 1;
        }

        var packagePath = await packageDownloadService.InstallAsync(requested, requestedVersion);
        AnsiConsole.MarkupLine(
            $"[green]Built and installed package:[/] {Markup.Escape(packagePath)}"
        );
        return 0;
    }

    private static int ShowUnknownCommand(string command)
    {
        AnsiConsole.MarkupLine($"[red]Unknown command:[/] {Markup.Escape(command)}");
        ShowHelp();
        return 1;
    }

    private static int ShowHttpServeHelp()
    {
        AnsiConsole.WriteLine("serve-http is started by the web host entry point.");
        return 1;
    }

    private static void ShowHelp()
    {
        AnsiConsole.MarkupLine("[bold]GroundKit[/]");
        AnsiConsole.MarkupLine("Use [aqua]groundkit --command[/] for shorter command syntax.");
        AnsiConsole.MarkupLine("Commands:");
        AnsiConsole.WriteLine(
            "  add <source> [--path path] [--name name] [--pkg-version version] [--save path] [--tag tag] [--choose-tag]"
        );
        AnsiConsole.MarkupLine("  import <package-file>");
        AnsiConsole.MarkupLine("  export <package-id> <destination>");
        AnsiConsole.MarkupLine("  list");
        AnsiConsole.MarkupLine("  inspect <package-id>");
        AnsiConsole.MarkupLine("  query <package-id> <topic>");
        AnsiConsole.MarkupLine("  refresh <package-id>");
        AnsiConsole.MarkupLine("  remove <package-id>");
        AnsiConsole.WriteLine("  serve [--libs package-a,package-b]");
        AnsiConsole.WriteLine("  serve-http [--urls http://localhost:3001]");
        AnsiConsole.WriteLine("  catalog [query]");
        AnsiConsole.WriteLine("  search-packages <registry> <name> [version]");
        AnsiConsole.WriteLine("  download-package <registry> <name> <version>");
        AnsiConsole.WriteLine("  install <registry/name|name|source> [version]");
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

    private static string NormalizeCommand(string command) =>
        command.ToLowerInvariant() switch
        {
            "--add" => "add",
            "--import" => "import",
            "--export" => "export",
            "--list" => "list",
            "--inspect" => "inspect",
            "--query" => "query",
            "--refresh" => "refresh",
            "--remove" => "remove",
            "--serve" => "serve",
            "--serve-http" => "serve-http",
            "--search-packages" => "search-packages",
            "--download-package" => "download-package",
            "--install" => "install",
            _ => command.ToLowerInvariant(),
        };

    private static bool HasFlag(IReadOnlyList<string> args, string optionName) =>
        args.Any(argument =>
            string.Equals(argument, optionName, StringComparison.OrdinalIgnoreCase)
        );
}
