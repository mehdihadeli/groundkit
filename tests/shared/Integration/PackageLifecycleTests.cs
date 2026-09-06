using GroundKit.Core.Contracts;
using GroundKit.Ingestion.Services;
using GroundKit.Storage.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace GroundKitShared.Tests.Integration;

public sealed class PackageLifecycleTests : IDisposable
{
    private readonly string _tempRoot;

    public PackageLifecycleTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Should_Build_Save_And_Query_Local_Directory_Package()
    {
        var docsPath = Path.Combine(_tempRoot, "docs");
        Directory.CreateDirectory(docsPath);
        await File.WriteAllTextAsync(
            Path.Combine(docsPath, "intro.md"),
            "# Introduction\n\nGroundKit is a local-first documentation MCP."
        );

        var builder = new DocumentPackageBuilder(
            new SourceDetector(),
            new TestHttpClientFactory(),
            NullLogger<DocumentPackageBuilder>.Instance
        );
        var buildResult = await builder.BuildAsync(docsPath);

        var store = new SqlitePackageStore(
            new PackageStoreOptions(Path.Combine(_tempRoot, "packages")),
            NullLogger<SqlitePackageStore>.Instance
        );
        var packagePath = await store.SaveAsync(buildResult);

        var response = await store.QueryAsync(
            new DocsQueryRequest(buildResult.Manifest.PackageId, "local-first")
        );

        File.Exists(packagePath).ShouldBeTrue();
        response.Hits.ShouldNotBeEmpty();
        response.Hits.ShouldContain(hit =>
            hit.Content.Contains("local-first", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task Should_Round_Trip_Exported_And_Imported_Package()
    {
        var docsPath = Path.Combine(_tempRoot, "docs");
        Directory.CreateDirectory(docsPath);
        await File.WriteAllTextAsync(Path.Combine(docsPath, "page.md"), "# Page\n\nContent.");

        var builder = new DocumentPackageBuilder(
            new SourceDetector(),
            new TestHttpClientFactory(),
            NullLogger<DocumentPackageBuilder>.Instance
        );
        var buildResult = await builder.BuildAsync(docsPath);

        var sourceStore = new SqlitePackageStore(
            new PackageStoreOptions(Path.Combine(_tempRoot, "source-packages")),
            NullLogger<SqlitePackageStore>.Instance
        );
        await sourceStore.SaveAsync(buildResult);

        var exportPath = Path.Combine(_tempRoot, "exports", "exported.db");
        await sourceStore.ExportAsync(buildResult.Manifest.PackageId, exportPath);

        var destinationStore = new SqlitePackageStore(
            new PackageStoreOptions(Path.Combine(_tempRoot, "destination-packages")),
            NullLogger<SqlitePackageStore>.Instance
        );
        var importedPath = await destinationStore.ImportAsync(exportPath);

        var packages = await destinationStore.ListAsync();
        packages.ShouldHaveSingleItem();
        File.Exists(importedPath).ShouldBeTrue();

        var response = await destinationStore.QueryAsync(
            new DocsQueryRequest(buildResult.Manifest.PackageId, "Content")
        );
        response.Hits.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Should_Remove_Package_File()
    {
        var docsPath = Path.Combine(_tempRoot, "docs");
        Directory.CreateDirectory(docsPath);
        await File.WriteAllTextAsync(Path.Combine(docsPath, "page.md"), "# Page\n\nContent.");

        var builder = new DocumentPackageBuilder(
            new SourceDetector(),
            new TestHttpClientFactory(),
            NullLogger<DocumentPackageBuilder>.Instance
        );
        var buildResult = await builder.BuildAsync(docsPath);

        var store = new SqlitePackageStore(
            new PackageStoreOptions(Path.Combine(_tempRoot, "packages")),
            NullLogger<SqlitePackageStore>.Instance
        );
        var packagePath = await store.SaveAsync(buildResult);

        var removed = await store.RemoveAsync(buildResult.Manifest.PackageId);

        removed.ShouldBe(1);
        File.Exists(packagePath).ShouldBeFalse();
    }

    [Fact]
    public async Task Should_Trim_Query_Hits_To_Token_Budget()
    {
        var docsPath = Path.Combine(_tempRoot, "docs");
        Directory.CreateDirectory(docsPath);
        await File.WriteAllTextAsync(
            Path.Combine(docsPath, "long.md"),
            "# Section 1\n\n" + new string('a', 400) + "\n\n# Section 2\n\n" + new string('b', 400)
        );

        var builder = new DocumentPackageBuilder(
            new SourceDetector(),
            new TestHttpClientFactory(),
            NullLogger<DocumentPackageBuilder>.Instance
        );
        var buildResult = await builder.BuildAsync(docsPath);

        var store = new SqlitePackageStore(
            new PackageStoreOptions(Path.Combine(_tempRoot, "packages")),
            NullLogger<SqlitePackageStore>.Instance
        );
        await store.SaveAsync(buildResult);

        var response = await store.QueryAsync(
            new DocsQueryRequest(
                buildResult.Manifest.PackageId,
                "section",
                new RetrievalOptions(MaxTokens: 150, MaxHits: 10, RelativeScoreCutoff: 0.1)
            )
        );

        response.Hits.Count.ShouldBeGreaterThanOrEqualTo(1);
        response.TotalTokens.ShouldBeLessThanOrEqualTo(150);
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new HttpClient(new TestMessageHandler());
    }

    private sealed class TestMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }
    }
}
