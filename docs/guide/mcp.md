# MCP setup

Start the local server:

```bash
dotnet run --project src/DocsContext.AppHost -- serve
```

For an installed or published executable, configure an MCP client with:

```json
{
  "mcpServers": {
    "docs-context": {
      "command": "docs-context",
      "args": ["serve"]
    }
  }
}
```

## Recommended agent flow

1. Call `resolve-source` or `library_catalog`.
2. If the package is missing, call `search_packages`.
3. Download a matching version with `download_package`.
4. Call `get_docs` with a short API name or topic.

The compatibility tool names mirror the common Context7 workflow, while the implementation remains local-first and vendor-neutral.
