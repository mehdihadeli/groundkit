using System.Text.Json;
using DocsContext.Core.Abstractions;
using DocsContext.Core.Contracts;
using DocsContext.Ingestion.Services;
using DocsContext.Mcp;
using DocsContext.Storage.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DocsContext.Tests.Integration;

public sealed class McpServerIntegrationTests : IDisposable
{
    private readonly string _tempRoot;

    public McpServerIntegrationTests()
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
    public async Task QueryDocs_EndToEnd_ReturnsHitFromBuiltPackage()
    {
        var packageStore = await CreatePopulatedStoreAsync();
        var responseAgent = new DocsContextToolResponseAgent(
            NullLoggerFactory.Instance,
            new ServiceCollection().BuildServiceProvider()
        );

        var result = await DocsContextMcpTools.QueryDocsAsync(
            "docs",
            "local-first",
            packageStore,
            responseAgent,
            CancellationToken.None,
            maxTokens: 500,
            maxHits: 5,
            relativeScoreCutoff: 0.1
        );

        Assert.NotEqual(true, result.IsError);
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("docs", structured.GetProperty("packageId").GetString());
        Assert.True(structured.GetProperty("hits").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task ResolveSource_EndToEnd_ReturnsInstalledPackage()
    {
        var packageStore = await CreatePopulatedStoreAsync();
        var responseAgent = new DocsContextToolResponseAgent(
            NullLoggerFactory.Instance,
            new ServiceCollection().BuildServiceProvider()
        );

        var result = await DocsContextMcpTools.ResolveSourceAsync(
            "docs",
            packageStore,
            responseAgent,
            CancellationToken.None,
            maxResults: 5
        );

        Assert.NotEqual(true, result.IsError);
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal(1, structured.GetProperty("packages").GetArrayLength());
    }

    private async Task<IPackageStore> CreatePopulatedStoreAsync()
    {
        var docsPath = Path.Combine(_tempRoot, "docs");
        Directory.CreateDirectory(docsPath);
        await File.WriteAllTextAsync(
            Path.Combine(docsPath, "intro.md"),
            "# Introduction\n\nDocsContext is a local-first documentation MCP."
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
        return store;
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new HttpClient(new TestMessageHandler());
    }

    private sealed class TestMessageHandler : System.Net.Http.HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }
    }
}
