using System.Text.Json;
using DocsContext.Core.Abstractions;
using DocsContext.Core.Contracts;
using DocsContext.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DocsContext.Tests.Integration;

public sealed class DocsContextMcpToolsIntegrationTests
{
    [Fact]
    public async Task QueryDocs_ReturnsStructuredHitPayload()
    {
        var packageStore = new RecordingPackageStore();
        var responseAgent = new DocsContextToolResponseAgent(
            NullLoggerFactory.Instance,
            new ServiceCollection().BuildServiceProvider()
        );

        var result = await DocsContextMcpTools.QueryDocsAsync(
            "docscontext",
            "refresh package",
            packageStore,
            responseAgent,
            CancellationToken.None,
            maxTokens: 500,
            maxHits: 5,
            relativeScoreCutoff: 0.5
        );

        Assert.NotEqual(true, result.IsError);
        Assert.NotEmpty(result.Content);
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("docscontext", structured.GetProperty("packageId").GetString());
        Assert.Equal(1, structured.GetProperty("hits").GetArrayLength());
    }

    [Fact]
    public async Task ResolveSource_ReturnsMatchPayload()
    {
        var packageStore = new RecordingPackageStore();
        var responseAgent = new DocsContextToolResponseAgent(
            NullLoggerFactory.Instance,
            new ServiceCollection().BuildServiceProvider()
        );

        var result = await DocsContextMcpTools.ResolveSourceAsync(
            "DocsContext",
            packageStore,
            responseAgent,
            CancellationToken.None,
            maxResults: 5
        );

        Assert.NotEqual(true, result.IsError);
        Assert.NotEmpty(result.Content);
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal(1, structured.GetProperty("packages").GetArrayLength());
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
        ) =>
            Task.FromResult(
                Packages.FirstOrDefault(package =>
                    package.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase)
                )
            );

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