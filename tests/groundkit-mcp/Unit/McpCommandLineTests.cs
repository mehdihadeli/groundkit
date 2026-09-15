using GroundKit.Mcp;

namespace GroundKit.Mcp.Tests.Unit;

public sealed class McpCommandLineTests
{
    [Theory]
    [InlineData("h", "http")]
    [InlineData("http", "http")]
    public void Should_Normalize_Http_Command_Alias(string alias, string expected)
    {
        McpCommandLine.NormalizeArguments([alias])[0].ShouldBe(expected);
    }

    [Fact]
    public void Should_Normalize_Short_Option_Aliases()
    {
        var normalized = McpCommandLine.NormalizeArguments(
            ["h", "-u", "http://localhost:3001", "-l", "react,vite"]
        );

        normalized.ShouldBe(["http", "--urls", "http://localhost:3001", "--libs", "react,vite"]);
    }
}
