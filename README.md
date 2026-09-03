# DocsContext

DocsContext is a local-first documentation MCP for AI agents, built in .NET.

DocsContext builds documentation once, stores it as portable local packages, and serves it to AI agents through MCP without depending on a hosted documentation service.

The core model is simple:

- Ingest docs from a source such as a git repo, local folder, `llms.txt` site, or raw page.
- Normalize and chunk that content into a retrieval-friendly package.
- Store the package as a local SQLite `.db` file.
- Let MCP clients query those local packages repeatedly with low latency.

## Product direction

DocsContext is meant to cover the full local documentation workflow:

1. Detect a documentation source from a local folder, git repo, `llms.txt` site, or raw page.
2. Build a portable local package once for a specific source snapshot.
3. Store that package in SQLite with retrieval-friendly chunk metadata.
4. Expose MCP tools that let an agent resolve sources and query focused sections.
5. Keep the common path fully local after ingestion.
6. Add enough CLI and lifecycle tooling that teams can build, install, update, inspect, and share packages.

## Coverage map

### Implemented now

- .NET 10 solution split into ingestion, storage, MCP, observability, app host, and tests.
- Local directory ingestion.
- Git repository ingestion through clone-to-temp build flow.
- `llms.txt` and `llms-full.txt` ingestion.
- Raw page ingestion with HTML-to-text fallback handling.
- Automatic source detection for local folder, git repo, `llms.txt`, and raw page inputs.
- Package build pipeline producing manifests, documents, chunks, fingerprints, and warnings.
- SQLite-backed package store.
- Lexical retrieval with max token budget, max hits, and relative score cutoff.
- MCP stdio server.
- MCP tools for source resolution and docs querying.
- Context7-style MCP compatibility tools: `get_docs` and `library_catalog`.
- Curated starter catalog of 20 popular JavaScript and web libraries.
- CLI commands for `add`, `list`, `query`, `refresh`, `remove`, `serve`, `inspect`, `export`, and `import`.
- Portable package import/export using SQLite `.db` artifacts.
- Basic OpenTelemetry tracing and metrics hooks.

### Next to build

- Better version selection for git sources: tags, branches, explicit package version labels.
- Better docs-root discovery and stronger repository heuristics.
- Smarter markdown and HTML chunking closer to docs structure instead of simple section splitting only.
- Adjacent-chunk merge in retrieval so agents get coherent sections rather than isolated fragments.
- Package metadata inspection and health diagnostics.
- Narrow integration and unit tests around ingestion, storage, and MCP tool contracts.

### Out of scope for current phase

- Hosted registry.
- Team sync service.
- HTTP transport.
- Embeddings or vector search.
- Semantic or graph retrieval.
- Multi-tenant or cloud deployment concerns.

## Current architecture

Current repo shape already matches the local-first core:

- Ingestion builds packages from source material.
- Storage persists package data under `.docs-context/packages` or `DOCSCONTEXT_HOME/packages`.
- MCP exposes query tools over stdio.
- Observability keeps the core path measurable.

That means the project is past the idea stage. Main gap now is not core direction, but finishing the developer workflow around package lifecycle and tightening retrieval quality.

## How the MCP server works

DocsContext follows a two-phase model:

1. Build a docs package once.
2. Store it locally as SQLite.
3. Let the MCP server answer future agent queries from local data instead of a hosted docs API.

In other words, ingestion happens ahead of time and retrieval happens on demand.

### Runtime flow

When you run `serve`, the app host starts a stdio MCP server. The MCP registration comes from the `DocsContext.Mcp` assembly, where tool methods are discovered and exposed through the Model Context Protocol server SDK.

At runtime the agent talks to DocsContext over stdio, not HTTP. The server does not fetch the internet during the normal query path. It assumes packages have already been built and installed locally under the package store directory.

That keeps the hot path small:

1. MCP client sends a tool call.
2. DocsContext loads package metadata or opens a local SQLite package.
3. DocsContext returns structured results and a compact readable summary.

### Tool surface

Current MCP surface is intentionally small:

- `resolve-source`: finds installed packages by package id or display name.
- `query-docs`: searches one installed package for relevant sections under a token budget.

This keeps the server focused on retrieval. Package creation, refresh, import, and export happen in the CLI workflow, while the MCP layer serves already-installed packages.

### What happens on `resolve-source`

`resolve-source` loads the installed package list from the local package store and performs simple lexical matching against package id and display name. It scores exact matches highest, then partial matches, then term overlap.

The result is returned in two forms:

- Structured JSON payload for MCP-aware clients.
- Concise plain-text summary for agents that benefit from readable tool output.

That dual-output pattern is important in this repo: tool calls return machine-readable data, but also a compact human-readable rendering generated by the local response formatter.

### What happens on `query-docs`

`query-docs` takes a package id and a topic. The package store opens the package's SQLite database and runs an FTS5 search against indexed chunk content.

Current retrieval path is:

1. Build FTS query from topic text.
2. Search the local SQLite FTS table.
3. Rank hits with BM25 weighting.
4. Drop weak results using a relative score cutoff.
5. Trim the final result set to the requested token budget.
6. Return both structured hit data and concise plain-text summaries.

This keeps the retrieval path fast and fully local. Expensive work belongs in package build time, not in every MCP call.

### Why response formatting is split from retrieval

This repo separates retrieval from presentation.

- `IPackageStore` and the SQLite store own package lookup and search.
- MCP tools own argument validation and tool contracts.
- `DocsContextToolResponseAgent` converts structured payloads into compact plain text.

That means the MCP layer can serve both structured clients and text-oriented agents without changing the retrieval engine.

### End-to-end mental model

You can think of DocsContext as two phases:

1. Offline or pre-query phase: `add` builds a package from git, local docs, `llms.txt`, or a raw page and stores it as a local SQLite `.db` file.
2. Online query phase: `serve` exposes MCP tools that resolve packages and query those local SQLite packages with no hosted dependency.

That is the core architectural idea behind DocsContext: build once, query locally many times.

## Current CLI surface

Current commands:

- `add <source> [--docs-path <path>]`
- `import <package-file>`
- `export <package-id> <destination>`
- `list`
- `inspect <package-id>`
- `query <package-id> <topic>`
- `refresh <package-id>`
- `remove <package-id>`
- `serve`
- `catalog [query]`
- `search-packages <registry> <name> [version]`
- `download-package <registry> <name> <version>`

That is enough for a first local-first workflow: build package, inspect it, query it, export it, import it elsewhere, and serve it to an MCP client.

### Starter catalog

List the initial curated sources:

```bash
dotnet run --project src/DocsContext.AppHost -- catalog
dotnet run --project src/DocsContext.AppHost -- catalog react
```

Catalog names can be passed directly to `add`. DocsContext expands them to their
known repository and documentation path before building a local package:

```bash
dotnet run --project src/DocsContext.AppHost -- add react
```

The MCP server exposes `library_catalog` for discovery and `get_docs` as the
Context7-compatible name for the existing local retrieval tool. `resolve-source`
and `query-docs` remain available for existing clients.

### Hosted package workflow

DocsContext can use any Context-compatible package registry. The default endpoint
is `http://localhost:8080`; set `DOCSCONTEXT_REGISTRY_URL` to use an internal,
production, or self-hosted server with the same API.

```bash
dotnet run --project src/DocsContext.AppHost -- search-packages npm react
dotnet run --project src/DocsContext.AppHost -- download-package npm react 19.1.0
dotnet run --project src/DocsContext.AppHost -- query react "useEffect cleanup"
```

MCP agents can follow the same sequence with `search_packages`,
`download_package`, and `get_docs`. Downloaded `.db` packages are imported into
the local store and then queried without a network call.

## Near-term roadmap

1. Tighten package lifecycle UX and semantics: naming, versioning, inspection, and refresh behavior.
2. Tighten build fidelity: docs path discovery, source versioning, better chunking, dedupe, and warnings.
3. Tighten retrieval quality: title weighting, adjacent merge, better formatting for agent consumption.
4. Expand tests around end-to-end local package build, export/import, and query behavior.
5. Consider optional later-phase features only after the local lexical path feels solid.

## Success criteria

DocsContext is successful when a developer can:

1. Point the tool at a repo, local docs folder, `llms.txt` site, or raw page.
2. Build a local package once.
3. Run the MCP server locally.
4. Let an agent resolve installed sources and query docs with no hosted dependency.
5. Share the resulting package artifact with another machine without rebuilding from scratch.
