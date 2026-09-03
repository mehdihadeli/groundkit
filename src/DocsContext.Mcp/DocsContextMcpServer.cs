using Microsoft.Extensions.Logging;

namespace DocsContext.Mcp;

using ModelContextProtocol.Server;

public sealed class DocsContextMcpServer(McpServer server, ILogger<DocsContextMcpServer> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting DocsContext MCP stdio host via ModelContextProtocol.");
        await server.RunAsync(cancellationToken);
    }
}
