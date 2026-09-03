using System.Net;
using DocsContext.Core.Contracts;
using DocsContext.Ingestion.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DocsContext.Tests.Unit;

public sealed class DocumentPackageBuilderTests : IDisposable
{
    private readonly string _tempRoot;

    public DocumentPackageBuilderTests()
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
    public async Task BuildAsync_LocalDirectory_CreatesManifestAndChunks()
    {
        var docsPath = Path.Combine(_tempRoot, "docs");
        Directory.CreateDirectory(docsPath);
        await File.WriteAllTextAsync(
            Path.Combine(docsPath, "intro.md"),
            "# Introduction\n\nThis is the intro.\n\n## Details\n\nMore details here."
        );

        var builder = CreateBuilder();
        var result = await builder.BuildAsync(docsPath);

        Assert.Equal(SourceKind.LocalDirectory, result.Source.Kind);
        Assert.Equal("docs", result.Manifest.DisplayName);
        Assert.True(result.Manifest.DocumentCount >= 1);
        Assert.True(result.Manifest.ChunkCount >= 2);
        Assert.All(result.Chunks, chunk => Assert.False(string.IsNullOrWhiteSpace(chunk.ChunkId)));
    }

    [Fact]
    public async Task BuildAsync_LocalDirectory_NoDocsFolder_UsesRoot()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempRoot, "readme.md"), "# Readme\n\nContent.");

        var builder = CreateBuilder();
        var result = await builder.BuildAsync(_tempRoot);

        Assert.True(result.Manifest.DocumentCount >= 1);
        Assert.True(result.Manifest.ChunkCount >= 1);
    }

    [Fact]
    public async Task BuildAsync_LocalDirectory_EmptyDocs_ThrowsInvalidOperationException()
    {
        var emptyPath = Path.Combine(_tempRoot, "empty");
        Directory.CreateDirectory(emptyPath);

        var builder = CreateBuilder();

        await Assert.ThrowsAsync<InvalidOperationException>(() => builder.BuildAsync(emptyPath));
    }

    [Fact]
    public async Task BuildAsync_LocalDirectory_SkipsExcludedPaths()
    {
        var docsPath = Path.Combine(_tempRoot, "docs");
        Directory.CreateDirectory(Path.Combine(docsPath, "node_modules"));
        await File.WriteAllTextAsync(
            Path.Combine(docsPath, "node_modules", "hidden.md"),
            "# Hidden\n\nShould be skipped."
        );
        await File.WriteAllTextAsync(
            Path.Combine(docsPath, "visible.md"),
            "# Visible\n\nShould be included."
        );

        var builder = CreateBuilder();
        var result = await builder.BuildAsync(docsPath);

        Assert.DoesNotContain(result.Documents, document => document.Path.Contains("node_modules"));
        Assert.Contains(result.Documents, document => document.Path.Contains("visible.md"));
    }

    [Fact]
    public async Task BuildAsync_LocalDirectory_DeduplicatesIdenticalSections()
    {
        var docsPath = Path.Combine(_tempRoot, "docs");
        Directory.CreateDirectory(docsPath);
        await File.WriteAllTextAsync(
            Path.Combine(docsPath, "dup.md"),
            "# Title\n\nSame paragraph.\n\n# Title\n\nSame paragraph."
        );

        var builder = CreateBuilder();
        var result = await builder.BuildAsync(docsPath);

        Assert.Contains(
            result.Warnings,
            warning => warning.Code == BuildWarningCode.DuplicateChunkSkipped
        );
    }

    private DocumentPackageBuilder CreateBuilder()
    {
        return new DocumentPackageBuilder(
            new SourceDetector(),
            new TestHttpClientFactory(),
            NullLogger<DocumentPackageBuilder>.Instance
        );
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new TestMessageHandler());
        }
    }

    private sealed class TestMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
