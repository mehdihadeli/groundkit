# MCP setup

Start the local server:

```bash
dotnet run --project src/groundkit-mcp
```

For an installed or published executable, configure an MCP client with:

```json
{
  "mcpServers": {
    "groundkit": {
      "command": "groundkit",
      "args": ["serve"]
    }
  }
}
```

## Recommended agent flow

1. Call `resolve-source` with the library name. Use `library_catalog` when choosing from starter sources.
2. Confirm returned package ID and version match the project dependency.
3. If the package is missing, call `search_packages` with registry, package name, and optional exact version.
4. Download the selected version with `download_package`. This is the only flow step that needs registry access.
5. Call `query-docs` with the installed package ID and a short API name, symbol, error code, or topic.
6. Answer from returned sections. If no useful hits return, resolve the source or refine the query; do not silently fall back to invented documentation.

`get_docs` is a compatibility alias for `query-docs`. Both return the same structured payload: package ID, version, token total, and focused hits with document and section titles, content, code presence, and relevance scores.

## Query shape

```json
{
  "packageId": "react",
  "topic": "useEffect cleanup",
  "maxTokens": 2000,
  "maxHits": 8,
  "relativeScoreCutoff": 0.5
}
```

Use a lower token or hit limit for a narrow lookup. Raise it only when the topic needs surrounding context. The score ranks matches; it is not a confidence or truth probability.

## Trust boundaries

The server queries installed packages locally and does not fetch documentation during `query-docs`. Package provenance and freshness come from the package manifest and source metadata. Registry results and downloads are external inputs, so pin versions and inspect source metadata when reproducibility matters.

MCP makes retrieval available to an agent; it does not make the final answer automatically correct. The host agent should distinguish missing evidence from a negative fact, preserve conflicting source information, and verify high-impact changes.
