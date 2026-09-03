# MCP tools

## `get_docs`

Primary documentation lookup. Takes `library`, `topic`, `maxTokens`, `maxHits`, and `relativeScoreCutoff`.

## `resolve-source`

Finds installed packages by package ID or display name.

## `library_catalog`

Lists the 20 curated starter sources, optionally filtered by name or description.

## `search_packages`

Searches a compatible registry by `registry`, `name`, and optional `version`.

## `download_package`

Downloads a registry package, imports it into the local store, and makes it available to `get_docs`.

All documentation results include structured content for MCP-aware clients and concise text for agents that consume tool output as prose.
