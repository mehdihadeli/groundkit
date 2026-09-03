using DocsContext.Core.Abstractions;
using DocsContext.Core.Contracts;
using DocsContext.Storage.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace DocsContext.Tests.Unit;

public sealed class SqlitePackageStoreTests : IDisposable
{
    private readonly string _rootPath;

    public SqlitePackageStoreTests()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_rootPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_CreatesPackageFile()
    {
        var store = CreateStore();
        var buildResult = CreateBuildResult("test-package");

        var path = await store.SaveAsync(buildResult);

        Assert.True(File.Exists(path));
        Assert.Contains(_rootPath, path);
    }

    [Fact]
    public async Task ListAsync_ReturnsSavedPackage()
    {
        var store = CreateStore();
        await store.SaveAsync(CreateBuildResult("test-package"));

        var packages = await store.ListAsync();

        Assert.Single(packages);
        Assert.Equal("test-package", packages[0].PackageId);
    }

    [Fact]
    public async Task GetPackageAsync_ExistingPackage_ReturnsSummary()
    {
        var store = CreateStore();
        await store.SaveAsync(CreateBuildResult("test-package"));

        var package = await store.GetPackageAsync("test-package");

        Assert.NotNull(package);
        Assert.Equal("test-package", package.PackageId);
    }

    [Fact]
    public async Task GetPackageAsync_MissingPackage_ReturnsNull()
    {
        var store = CreateStore();

        var package = await store.GetPackageAsync("missing");

        Assert.Null(package);
    }

    [Fact]
    public async Task RemoveAsync_ExistingPackage_DeletesFile()
    {
        var store = CreateStore();
        await store.SaveAsync(CreateBuildResult("test-package"));

        var removed = await store.RemoveAsync("test-package");

        Assert.Equal(1, removed);
        Assert.Empty(await store.ListAsync());
    }

    [Fact]
    public async Task ExportAsync_CopiesPackageFile()
    {
        var store = CreateStore();
        var path = await store.SaveAsync(CreateBuildResult("test-package"));
        var destination = Path.Combine(_rootPath, "exports");

        var exportPath = await store.ExportAsync("test-package", destination);

        Assert.True(File.Exists(exportPath));
        Assert.Equal(Path.GetFileName(path), Path.GetFileName(exportPath));
    }

    [Fact]
    public async Task ImportAsync_CopiesPackageIntoStore()
    {
        var store = CreateStore();
        var path = await store.SaveAsync(CreateBuildResult("test-package"));
        var importSource = Path.Combine(_rootPath, "import", Path.GetFileName(path));
        Directory.CreateDirectory(Path.GetDirectoryName(importSource)!);
        File.Copy(path, importSource);

        var importedPath = await store.ImportAsync(importSource);

        Assert.True(File.Exists(importedPath));
        Assert.Contains(_rootPath, importedPath);
    }

    [Fact]
    public async Task QueryAsync_ExistingPackage_ReturnsHits()
    {
        var store = CreateStore();
        await store.SaveAsync(CreateBuildResult("test-package", "# Getting Started\n\nUse refresh to rebuild."));

        var response = await store.QueryAsync(new DocsQueryRequest("test-package", "refresh"));

        Assert.NotEmpty(response.Hits);
        Assert.True(response.TotalTokens > 0);
    }

    [Fact]
    public async Task QueryAsync_MissingPackage_ThrowsInvalidOperationException()
    {
        var store = CreateStore();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.QueryAsync(new DocsQueryRequest("missing", "topic")));
    }

    [Fact]
    public async Task GetSourceAsync_ExistingPackage_ReturnsSource()
    {
        var store = CreateStore();
        await store.SaveAsync(CreateBuildResult("test-package"));

        var source = await store.GetSourceAsync("test-package");

        Assert.NotNull(source);
        Assert.Equal(SourceKind.LocalDirectory, source.Kind);
    }

    private SqlitePackageStore CreateStore() => new(new PackageStoreOptions(_rootPath), NullLogger<SqlitePackageStore>.Instance);

    private static BuildResult CreateBuildResult(string packageId, string content = "# Title\n\nBody.")
    {
        var source = new DocumentationSource(SourceKind.LocalDirectory, packageId, packageId, "C:/test");
        var document = new DocumentRecord("doc-1", "Title", "title.md", content);
        var chunk = new ChunkRecord(
            "chunk-1",
            "doc-1",
            "Title",
            "Title",
            "title.md",
            content,
            10,
            false,
            "hash-1",
            0
        );

        return new BuildResult(
            source,
            new PackageManifest(
                packageId,
                packageId,
                "dev",
                SourceKind.LocalDirectory,
                "C:/test",
                packageId,
                "fingerprint",
                DateTimeOffset.UtcNow,
                "1.0.0",
                1,
                1,
                0
            ),
            [],
            [document],
            [chunk]
        );
    }
}
