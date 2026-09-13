using System.Text.Json;
using GroundKit.Core.Abstractions;
using GroundKit.Core.Contracts;
using GroundKit.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;

namespace GroundKit.Mcp.Tests.Unit;

public sealed class GroundKitMcpToolsTests
{
    [Fact]
    public async Task Should_Reject_Empty_Source_Query()
    {
        var result = await GroundKitMcpTools.ResolveSourceAsync(
            "",
            Substitute.For<IPackageStore>(),
            CreateAgent(),
            CancellationToken.None
        );

        result.IsError.ShouldBe(true);
        GetText(result.Content).ShouldContain("'query'");
    }

    [Fact]
    public async Task Should_Return_Matching_Source_As_Structured_Content()
    {
        var store = Substitute.For<IPackageStore>();
        store
            .ListAsync(Arg.Any<CancellationToken>())
            .Returns(
                [
                    new PackageSummary(
                        "react",
                        "React",
                        "19.0.0",
                        2,
                        4,
                        DateTimeOffset.UtcNow,
                        "/packages/react.db"
                    ),
                ]
            );

        var result = await GroundKitMcpTools.ResolveSourceAsync(
            "react",
            store,
            CreateAgent(),
            CancellationToken.None
        );

        result.IsError.ShouldNotBe(true);
        var structured = result.StructuredContent.ShouldBeOfType<JsonElement>();
        structured.GetProperty("packages").GetArrayLength().ShouldBe(1);
        structured
            .GetProperty("packages")[0]
            .GetProperty("packageId")
            .GetString()
            .ShouldBe("react");
    }

    [Theory]
    [InlineData("", "topic", "packageId")]
    [InlineData("react", "", "topic")]
    public async Task Should_Reject_Missing_Document_Query_Arguments(
        string packageId,
        string topic,
        string expectedArgument
    )
    {
        var result = await GroundKitMcpTools.QueryDocsAsync(
            packageId,
            topic,
            Substitute.For<IPackageStore>(),
            CreateAgent(),
            CancellationToken.None
        );

        result.IsError.ShouldBe(true);
        GetText(result.Content).ShouldContain(expectedArgument);
    }

    private static GroundKitToolResponseAgent CreateAgent() =>
        new(NullLoggerFactory.Instance, new ServiceCollection().BuildServiceProvider());

    private static string GetText(IList<ContentBlock> content) =>
        content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? string.Empty;
}
