using GroundKit.AppHost;
using GroundKit.Core.Contracts;
using GroundKit.Ingestion.Services;
using GroundKit.Mcp;
using GroundKit.Mcp.DependencyInjection;
using GroundKit.Storage.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GroundKit.Tests.Integration;

public sealed class AddFromWebsiteIntegrationTests : IDisposable
{
    private const string WebsiteRoot = "https://agentgateway.dev";
    private const string LlmsTextUrl = "https://agentgateway.dev/llms.txt";
    private const string BlogUrl =
        "https://agentgateway.dev/blog/2026-08-20-benchmarking-agentgateway-epp-proxy-overhead/";
    private const string GitHubReadmeUrl =
        "https://github.com/agentgateway/agentgateway/blob/main/README.md";
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"groundkit-website-add-{Guid.NewGuid():N}"
    );

    public AddFromWebsiteIntegrationTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Should_Add_Website_Root_And_Auto_Detect_Llms_Text()
    {
        var packageStore = CreatePackageStore("root");
        var exitCode = await CreateApplication(packageStore).RunAsync(["add", WebsiteRoot]);

        var package = (
            await packageStore.ListAsync(TestContext.Current.CancellationToken)
        ).ShouldHaveSingleItem();
        var source = await packageStore.GetSourceAsync(
            package.PackageId,
            TestContext.Current.CancellationToken
        );

        exitCode.ShouldBe(0);
        source.ShouldNotBeNull();
        source.Kind.ShouldBe(SourceKind.LlmsText);
        source.Location.ShouldBe(WebsiteRoot);
        package.DocumentCount.ShouldBeGreaterThan(1);
        package.ChunkCount.ShouldBeGreaterThan(1);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Should_Add_Direct_Llms_Text_With_Custom_Name()
    {
        var packageStore = CreatePackageStore("direct");
        var exitCode = await CreateApplication(packageStore)
            .RunAsync(["add", LlmsTextUrl, "--name", "agent-gateway"]);

        var package = (
            await packageStore.ListAsync(TestContext.Current.CancellationToken)
        ).ShouldHaveSingleItem();
        var source = await packageStore.GetSourceAsync(
            package.PackageId,
            TestContext.Current.CancellationToken
        );

        exitCode.ShouldBe(0);
        package.PackageId.ShouldBe("agent-gateway");
        package.DisplayName.ShouldBe("agent-gateway");
        source.ShouldNotBeNull();
        source.Kind.ShouldBe(SourceKind.LlmsText);
        source.Location.ShouldBe(LlmsTextUrl);
        source.CanonicalId.ShouldBe("agent-gateway");
        package.DocumentCount.ShouldBeGreaterThan(1);
        package.ChunkCount.ShouldBeGreaterThan(1);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Should_Save_Website_Package_Copy_With_Custom_Version()
    {
        var packageStore = CreatePackageStore("saved-copy");
        var savedCopyPath = Path.Combine(_root, "shared", "agent-gateway@1.0.0.db");
        var exitCode = await CreateApplication(packageStore)
            .RunAsync(
                [
                    "add",
                    LlmsTextUrl,
                    "--name",
                    "agent-gateway",
                    "--pkg-version",
                    "1.0.0",
                    "--save",
                    savedCopyPath,
                ]
            );

        var package = (
            await packageStore.ListAsync(TestContext.Current.CancellationToken)
        ).ShouldHaveSingleItem();

        exitCode.ShouldBe(0);
        package.PackageId.ShouldBe("agent-gateway");
        package.Version.ShouldBe("1.0.0");
        File.Exists(savedCopyPath).ShouldBeTrue();
        new FileInfo(savedCopyPath).Length.ShouldBeGreaterThan(0);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Should_Add_Arbitrary_Blog_Article_When_Llms_Is_Not_The_Source()
    {
        var packageStore = CreatePackageStore("blog");
        var exitCode = await CreateApplication(packageStore).RunAsync(["add", BlogUrl]);

        var package = (
            await packageStore.ListAsync(TestContext.Current.CancellationToken)
        ).ShouldHaveSingleItem();

        exitCode.ShouldBe(0);
        package.DocumentCount.ShouldBe(1);
        package.ChunkCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Should_Add_GitHub_Blob_Markdown_As_Arbitrary_URL()
    {
        var packageStore = CreatePackageStore("github-readme");
        var exitCode = await CreateApplication(packageStore)
            .RunAsync(["add", GitHubReadmeUrl, "--name", "agentgateway-readme"]);

        var package = (
            await packageStore.ListAsync(TestContext.Current.CancellationToken)
        ).ShouldHaveSingleItem();
        var source = await packageStore.GetSourceAsync(
            package.PackageId,
            TestContext.Current.CancellationToken
        );

        exitCode.ShouldBe(0);
        package.PackageId.ShouldBe("agentgateway-readme");
        package.DocumentCount.ShouldBe(1);
        package.ChunkCount.ShouldBeGreaterThan(0);
        source.ShouldNotBeNull();
        source.Kind.ShouldBe(SourceKind.RawPage);
        source.Location.ShouldBe(GitHubReadmeUrl);
    }

    private CliApplication CreateApplication(SqlitePackageStore packageStore)
    {
        return new CliApplication(
            new DocumentPackageBuilder(
                new SourceDetector(),
                new HttpClientFactory(),
                NullLogger<DocumentPackageBuilder>.Instance
            ),
            packageStore,
            CreateMcpServer()
        );
    }

    private SqlitePackageStore CreatePackageStore(string scenario)
    {
        return new SqlitePackageStore(
            new PackageStoreOptions(Path.Combine(_root, "packages", scenario)),
            NullLogger<SqlitePackageStore>.Instance
        );
    }

    private static GroundKitMcpServer CreateMcpServer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGroundKitMcp();
        return services.BuildServiceProvider().GetRequiredService<GroundKitMcpServer>();
    }

    private sealed class HttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
