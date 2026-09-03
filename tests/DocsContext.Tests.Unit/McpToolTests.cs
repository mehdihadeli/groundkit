using System.Text.Json;
using DocsContext.Core.Abstractions;
using DocsContext.Core.Contracts;
using DocsContext.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;

namespace DocsContext.Tests.Unit;

public sealed class McpToolTests
{
    [Fact]
    public async Task ResolveSourceAsync_ValidQuery_ReturnsMatch()
    {
        var store = new RecordingPackageStore();
        var agent = CreateAgent();

        var result = await DocsContextMcpTools.ResolveSourceAsync(
            "docscontext",
            store,
            agent,
            CancellationToken.None,
            maxResults: 5
        );

        Assert.NotEqual(true, result.IsError);
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal(1, structured.GetProperty("packages").GetArrayLength());
    }

    [Fact]
    public async Task ResolveSourceAsync_EmptyQuery_ReturnsError()
    {
        var store = new RecordingPackageStore();
        var agent = CreateAgent();

        var result = await DocsContextMcpTools.ResolveSourceAsync(
            "",
            store,
            agent,
            CancellationToken.None
        );

        Assert.Equal(true, result.IsError);
    }

    [Fact]
    public async Task QueryDocsAsync_ValidRequest_ReturnsHit()
    {
        var store = new RecordingPackageStore();
        var agent = CreateAgent();

        var result = await DocsContextMcpTools.QueryDocsAsync(
            "docscontext",
            "refresh",
            store,
            agent,
            CancellationToken.None,
            maxTokens: 500,
            maxHits: 5,
            relativeScoreCutoff: 0.5
        );

        Assert.NotEqual(true, result.IsError);
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("docscontext", structured.GetProperty("packageId").GetString());
        Assert.Equal(1, structured.GetProperty("hits").GetArrayLength());
    }

    [Theory]
    [InlineData("", "topic", "'packageId'")]
    [InlineData("package", "", "'topic'")]
    public async Task QueryDocsAsync_MissingRequiredArgument_ReturnsError(
        string packageId,
        string topic,
        string expectedInMessage
    )
    {
        var store = new RecordingPackageStore();
        var agent = CreateAgent();

        var result = await DocsContextMcpTools.QueryDocsAsync(
            packageId,
            topic,
            store,
            agent,
            CancellationToken.None
        );

        Assert.Equal(true, result.IsError);
        Assert.Contains(expectedInMessage, GetText(result.Content));
    }

    [Theory]
    [InlineData(0, 5, 0.5, "maxTokens")]
    [InlineData(100, 0, 0.5, "maxHits")]
    [InlineData(100, 5, -0.1, "relativeScoreCutoff")]
    [InlineData(100, 5, 1.1, "relativeScoreCutoff")]
    public async Task QueryDocsAsync_InvalidOptions_ReturnsError(
        int maxTokens,
        int maxHits,
        double relativeScoreCutoff,
        string expectedArgument
    )
    {
        var store = new RecordingPackageStore();
        var agent = CreateAgent();

        var result = await DocsContextMcpTools.QueryDocsAsync(
            "docscontext",
            "topic",
            store,
            agent,
            CancellationToken.None,
            maxTokens: maxTokens,
            maxHits: maxHits,
            relativeScoreCutoff: relativeScoreCutoff
        );

        Assert.Equal(true, result.IsError);
        Assert.Contains(expectedArgument, GetText(result.Content));
    }

    private static DocsContextToolResponseAgent CreateAgent() =>
        new(NullLoggerFactory.Instance, new ServiceCollection().BuildServiceProvider());

    private static string GetText(IList<ContentBlock> content)
    {
        var textBlock = content.OfType<TextContentBlock>().FirstOrDefault();
        return textBlock?.Text ?? string.Empty;
    }

    private sealed class RecordingPackageStore : IPackageStore
    {
        private static readonly IReadOnlyList<PackageSummary> Packages =
        [
            new PackageSummary(
                "docscontext",
                "DocsContext",
                "v1.2.3",
                3,
                12,
                DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
                "/packages/docscontext@v1.2.3.db"
            ),
        ];

        public Task<string> SaveAsync(
            BuildResult buildResult,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(string.Empty);

        public Task<string> ImportAsync(
            string packageFilePath,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(string.Empty);

        public Task<string> ExportAsync(
            string packageId,
            string destinationPath,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(string.Empty);

        public Task<IReadOnlyList<PackageSummary>> ListAsync(
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Packages);

        public Task<PackageSummary?> GetPackageAsync(
            string packageId,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Packages.FirstOrDefault());

        public Task<int> RemoveAsync(
            string packageId,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(0);

        public Task<DocumentationSource?> GetSourceAsync(
            string packageId,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<DocumentationSource?>(null);

        public Task<DocsQueryResponse> QueryAsync(
            DocsQueryRequest request,
            CancellationToken cancellationToken = default
        )
        {
            var response = new DocsQueryResponse(
                request.PackageId,
                "v1.2.3",
                [
                    new DocsQueryHit(
                        "Getting Started",
                        "Refresh Command",
                        "Use refresh to rebuild the installed package from stored source metadata.",
                        24,
                        false,
                        0.93
                    ),
                ],
                24
            );
            return Task.FromResult(response);
        }
    }
}
