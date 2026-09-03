using DocsContext.AppHost;
using DocsContext.Core.Abstractions;
using DocsContext.Core.Contracts;
using DocsContext.Mcp;
using DocsContext.Mcp.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DocsContext.Tests.Unit;

public sealed class CliApplicationTests
{
    [Fact]
    public async Task RunAsync_NoArgs_ShowsHelp()
    {
        var application = CreateApplication();

        var exitCode = await application.RunAsync([]);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task RunAsync_UnknownCommand_ReturnsError()
    {
        var application = CreateApplication();

        var exitCode = await application.RunAsync(["unknown"]);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task RunAsync_Add_MissingSource_ReturnsError()
    {
        var application = CreateApplication();

        var exitCode = await application.RunAsync(["add"]);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task RunAsync_Add_BuildsAndSavesPackage()
    {
        var source = new DocumentationSource(SourceKind.LocalDirectory, "docs", "Docs", "C:/docs");
        var buildResult = CreateBuildResult(source);
        var builder = new RecordingPackageBuilder(buildResult);
        var store = new RecordingPackageStore(source, "C:/packages/docs@dev.db");
        var application = new CliApplication(builder, store, CreateMcpServer());

        var exitCode = await application.RunAsync(["add", "C:/docs"]);

        Assert.Equal(0, exitCode);
        Assert.Equal("C:/docs", builder.Input);
        Assert.Null(builder.DocsPath);
        Assert.Equal(1, store.SaveCallCount);
    }

    [Fact]
    public async Task RunAsync_Add_WithDocsPath_PassesDocsPath()
    {
        var source = new DocumentationSource(
            SourceKind.LocalDirectory,
            "docs",
            "Docs",
            "C:/docs",
            "src"
        );
        var buildResult = CreateBuildResult(source);
        var builder = new RecordingPackageBuilder(buildResult);
        var store = new RecordingPackageStore(source, "C:/packages/docs@dev.db");
        var application = new CliApplication(builder, store, CreateMcpServer());

        var exitCode = await application.RunAsync(["add", "C:/docs", "--docs-path", "src"]);

        Assert.Equal(0, exitCode);
        Assert.Equal("C:/docs", builder.Input);
        Assert.Equal("src", builder.DocsPath);
    }

    [Fact]
    public async Task RunAsync_List_NoPackages_WritesMessage()
    {
        var application = CreateApplication();

        var exitCode = await application.RunAsync(["list"]);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task RunAsync_Remove_MissingPackageId_ReturnsError()
    {
        var application = CreateApplication();

        var exitCode = await application.RunAsync(["remove"]);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task RunAsync_Refresh_UsesStoredSourceMetadata()
    {
        var source = new DocumentationSource(
            SourceKind.GitRepository,
            "docscontext",
            "DocsContext",
            "https://example.invalid/docscontext.git",
            "docs",
            "v1.2.3",
            "v1.2.3",
            "main",
            "fingerprint-123",
            DateTimeOffset.Parse("2026-07-01T00:00:00Z")
        );
        var buildResult = CreateBuildResult(source);
        var builder = new RecordingPackageBuilder(buildResult);
        var store = new RecordingPackageStore(source, "/tmp/docscontext/docscontext@v1.2.3.db");
        var application = new CliApplication(builder, store, CreateMcpServer());

        var exitCode = await application.RunAsync(["refresh", "docscontext"]);

        Assert.Equal(0, exitCode);
        Assert.Equal("https://example.invalid/docscontext.git", builder.Input);
        Assert.Equal("docs", builder.DocsPath);
        Assert.Equal(1, store.SaveCallCount);
        Assert.Equal("docscontext", store.LastSavedPackageId);
    }

    [Fact]
    public async Task RunAsync_Export_UsesPackageStoreExport()
    {
        var source = new DocumentationSource(
            SourceKind.LocalDirectory,
            "docscontext",
            "DocsContext",
            "C:/docs-context/docs"
        );
        var builder = new RecordingPackageBuilder(CreateBuildResult(source));
        var store = new RecordingPackageStore(source, "/tmp/docscontext/docscontext@dev.db");
        var application = new CliApplication(builder, store, CreateMcpServer());

        var exitCode = await application.RunAsync(["export", "docscontext", "./artifacts"]);

        Assert.Equal(0, exitCode);
        Assert.Equal("docscontext", store.ExportPackageId);
        Assert.Equal("./artifacts", store.ExportDestination);
    }

    [Fact]
    public async Task RunAsync_Inspect_LoadsPackageAndSourceMetadata()
    {
        var source = new DocumentationSource(
            SourceKind.GitRepository,
            "docscontext",
            "DocsContext",
            "https://example.invalid/docscontext.git",
            "docs",
            "v1.2.3"
        );
        var builder = new RecordingPackageBuilder(CreateBuildResult(source));
        var store = new RecordingPackageStore(source, "/tmp/docscontext/docscontext@v1.2.3.db");
        var application = new CliApplication(builder, store, CreateMcpServer());

        var exitCode = await application.RunAsync(["inspect", "docscontext"]);

        Assert.Equal(0, exitCode);
        Assert.Equal("docscontext", store.LastRequestedPackageId);
        Assert.Equal(1, store.GetPackageCallCount);
        Assert.Equal(1, store.GetSourceCallCount);
    }

    private static CliApplication CreateApplication()
    {
        var source = new DocumentationSource(SourceKind.LocalDirectory, "docs", "Docs", "C:/docs");
        return new CliApplication(
            new RecordingPackageBuilder(CreateBuildResult(source)),
            new RecordingPackageStore(source, "C:/packages/docs@dev.db"),
            CreateMcpServer()
        );
    }

    private static DocsContextMcpServer CreateMcpServer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDocsContextMcp();
        return services.BuildServiceProvider().GetRequiredService<DocsContextMcpServer>();
    }

    private static BuildResult CreateBuildResult(DocumentationSource source)
    {
        return new BuildResult(
            source,
            new PackageManifest(
                source.CanonicalId,
                source.DisplayName,
                source.Version,
                source.Kind,
                source.Location,
                source.CanonicalId,
                source.Fingerprint,
                DateTimeOffset.UtcNow,
                "1.0.0",
                1,
                1,
                0
            ),
            [],
            [new DocumentRecord("doc-1", "Title", "title.md", "# Title")],
            [
                new ChunkRecord(
                    "chunk-1",
                    "doc-1",
                    "Title",
                    "Title",
                    "title.md",
                    "# Title",
                    2,
                    false,
                    "hash",
                    0
                ),
            ]
        );
    }

    private sealed class RecordingPackageBuilder(BuildResult buildResult) : IDocumentPackageBuilder
    {
        public string? Input { get; private set; }
        public string? DocsPath { get; private set; }

        public Task<BuildResult> BuildAsync(
            string input,
            string? docsPath = null,
            CancellationToken cancellationToken = default
        )
        {
            Input = input;
            DocsPath = docsPath;
            return Task.FromResult(buildResult);
        }
    }

    private sealed class RecordingPackageStore(DocumentationSource source, string packagePath)
        : IPackageStore
    {
        public int SaveCallCount { get; private set; }
        public string? LastSavedPackageId { get; private set; }
        public string? ExportPackageId { get; private set; }
        public string? ExportDestination { get; private set; }
        public string? LastRequestedPackageId { get; private set; }
        public int GetPackageCallCount { get; private set; }
        public int GetSourceCallCount { get; private set; }

        public Task<string> SaveAsync(
            BuildResult buildResult,
            CancellationToken cancellationToken = default
        )
        {
            SaveCallCount++;
            LastSavedPackageId = buildResult.Manifest.PackageId;
            return Task.FromResult(packagePath);
        }

        public Task<string> ImportAsync(
            string packageFilePath,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(packagePath);

        public Task<string> ExportAsync(
            string packageId,
            string destinationPath,
            CancellationToken cancellationToken = default
        )
        {
            ExportPackageId = packageId;
            ExportDestination = destinationPath;
            return Task.FromResult(Path.Combine(destinationPath, Path.GetFileName(packagePath)));
        }

        public Task<IReadOnlyList<PackageSummary>> ListAsync(
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<PackageSummary>>([]);

        public Task<PackageSummary?> GetPackageAsync(
            string packageId,
            CancellationToken cancellationToken = default
        )
        {
            LastRequestedPackageId = packageId;
            GetPackageCallCount++;
            return Task.FromResult<PackageSummary?>(
                new PackageSummary(
                    source.CanonicalId,
                    source.DisplayName,
                    source.Version,
                    1,
                    1,
                    DateTimeOffset.Parse("2026-07-06T00:00:00Z"),
                    packagePath
                )
            );
        }

        public Task<int> RemoveAsync(
            string packageId,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(0);

        public Task<DocumentationSource?> GetSourceAsync(
            string packageId,
            CancellationToken cancellationToken = default
        )
        {
            LastRequestedPackageId = packageId;
            GetSourceCallCount++;
            return Task.FromResult<DocumentationSource?>(source);
        }

        public Task<DocsQueryResponse> QueryAsync(
            DocsQueryRequest request,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new DocsQueryResponse(request.PackageId, null, [], 0));
    }
}
