# Integrations

## Connect any MCP-compatible host

GroundKit uses the standard stdio transport. The host starts the .NET process and communicates with its discovered tools over stdin and stdout.

## VS Code and Copilot

Add a workspace MCP configuration in `.vscode/mcp.json`:

```json
{
  "servers": {
    "groundkit": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "src/groundkit-mcp"]
    }
  }
}
```

Use the published executable in shared environments to avoid a build on every launch.

## Claude Desktop, Cursor, and other hosts

Most MCP clients accept a JSON server entry. Adapt the command shape to the host's configuration file:

```json
{
  "mcpServers": {
    "groundkit": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/groundkit-mcp"]
    }
  }
}
```

On Windows, use an absolute project path with forward or escaped backslashes. A published server can instead use `"command": "groundkit-mcp"`.

## Host-neutral contract

The integration only needs three facts:

- Command: starts the GroundKit MCP server.
- Transport: `stdio`.
- Tools: `resolve-source`, `get_docs`, `library_catalog`, `search_packages`, and `download_package`.

After connection, the recommended sequence is [resolve, search, download, query](/guide/mcp).
