# Repo-local development launcher for the GroundKit MCP server.
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $GroundKitMcpArgs
)

$project = Join-Path $PSScriptRoot "src/groundkit-mcp/GroundKit.Mcp.csproj"
dotnet run --project $project --no-restore -v:q -- @GroundKitMcpArgs
exit $LASTEXITCODE