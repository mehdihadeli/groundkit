using GroundKit.AppHost;
using GroundKit.Core.Contracts;
using GroundKit.Ingestion.Services;
using GroundKit.Mcp;
using GroundKit.Mcp.DependencyInjection;
using GroundKit.Storage.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GroundKit.Tests.Integration;

public sealed class AddWithGitRepositoryIntegrationTests : IDisposable
{
    private const string RepositoryUrl = "https://github.com/mattpocock/skills";
    private const string RepositoryTag = "v1.2.3";
    private readonly string _packageRoot = Path.Combine(
        Path.GetTempPath(),
        $"groundkit-git-packages-{Guid.NewGuid():N}"
    );

    public AddWithGitRepositoryIntegrationTests()
    {
        Directory.CreateDirectory(_packageRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_packageRoot))
        {
            Directory.Delete(_packageRoot, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Should_Add_Tagged_Git_Repository_And_Remove_Temporary_Clone()
    {
        var cloneDirectoriesBefore = SnapshotTemporaryCloneDirectories();
        var application = CreateApplication("tagged");

        var exitCode = await application.RunAsync(["add", RepositoryUrl, "--tag", RepositoryTag]);

        var packageStore = CreatePackageStore("tagged");
        var package = (
            await packageStore.ListAsync(TestContext.Current.CancellationToken)
        ).ShouldHaveSingleItem();
        var source = await packageStore.GetSourceAsync(
            package.PackageId,
            TestContext.Current.CancellationToken
        );

        exitCode.ShouldBe(0);
        source.ShouldNotBeNull();
        source.Kind.ShouldBe(SourceKind.GitRepository);
        source.Location.ShouldBe(RepositoryUrl);
        source.Tag.ShouldBe(RepositoryTag);
        package.DocumentCount.ShouldBeGreaterThan(0);
        package.ChunkCount.ShouldBeGreaterThan(0);
        SnapshotTemporaryCloneDirectories().ShouldBe(cloneDirectoriesBefore);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Should_Add_GitHub_Tree_Branch_And_Store_Requested_Branch()
    {
        var cloneDirectoriesBefore = SnapshotTemporaryCloneDirectories();
        var application = CreateApplication("branch");

        var exitCode = await application.RunAsync(["add", $"{RepositoryUrl}/tree/main"]);

        var packageStore = CreatePackageStore("branch");
        var package = (
            await packageStore.ListAsync(TestContext.Current.CancellationToken)
        ).ShouldHaveSingleItem();
        var source = await packageStore.GetSourceAsync(
            package.PackageId,
            TestContext.Current.CancellationToken
        );

        exitCode.ShouldBe(0);
        source.ShouldNotBeNull();
        source.Location.ShouldBe(RepositoryUrl);
        source.Tag.ShouldBe("main");
        package.DocumentCount.ShouldBeGreaterThan(0);
        package.ChunkCount.ShouldBeGreaterThan(0);
        SnapshotTemporaryCloneDirectories().ShouldBe(cloneDirectoriesBefore);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Should_Add_Git_Repository_With_Docs_Path_And_Store_Metadata()
    {
        var application = CreateApplication("docs-path");

        var exitCode = await application.RunAsync(
            ["add", RepositoryUrl, "--tag", RepositoryTag, "--docs-path", "skills"]
        );

        var packageStore = CreatePackageStore("docs-path");
        var package = (
            await packageStore.ListAsync(TestContext.Current.CancellationToken)
        ).ShouldHaveSingleItem();
        var source = await packageStore.GetSourceAsync(
            package.PackageId,
            TestContext.Current.CancellationToken
        );

        exitCode.ShouldBe(0);
        source.ShouldNotBeNull();
        source.DocsPath.ShouldBe("skills");
        source.Tag.ShouldBe(RepositoryTag);
        package.DocumentCount.ShouldBeGreaterThan(0);
        package.ChunkCount.ShouldBeGreaterThan(0);
    }

    private CliApplication CreateApplication(string scenario)
    {
        return new CliApplication(
            new DocumentPackageBuilder(
                new SourceDetector(),
                new EmptyHttpClientFactory(),
                NullLogger<DocumentPackageBuilder>.Instance
            ),
            CreatePackageStore(scenario),
            CreateMcpServer(),
            gitReferenceProvider: new GitReferenceProvider()
        );
    }

    private SqlitePackageStore CreatePackageStore(string scenario)
    {
        return new SqlitePackageStore(
            new PackageStoreOptions(Path.Combine(_packageRoot, scenario)),
            NullLogger<SqlitePackageStore>.Instance
        );
    }

    private static string[] SnapshotTemporaryCloneDirectories()
    {
        var root = Path.Combine(Environment.CurrentDirectory, ".tmp", "groundkit");
        return Directory.Exists(root)
            ? Directory.GetDirectories(root).OrderBy(path => path).ToArray()
            : [];
    }

    private static GroundKitMcpServer CreateMcpServer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGroundKitMcp();
        return services.BuildServiceProvider().GetRequiredService<GroundKitMcpServer>();
    }

    private sealed class EmptyHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
