# Quickstart

GroundKit turns documentation into local packages and serves those packages to MCP-compatible AI agents.

## Prerequisites

- .NET SDK 10
- Git, when building from a repository
- An MCP-compatible client such as VS Code, Claude Desktop, or Cursor

Before using the examples below during development, add the repository root to
`PATH` for the current shell session so `groundkit` resolves to the repo-local
wrapper:

```bash
export PATH="$PWD:$PATH"
```

```powershell
$env:Path = "$PWD;$env:Path"
```

## Build a package

Build documentation from a repository:

```bash
groundkit add https://github.com/vuejs/docs --docs-path src
```

Local repositories can use automatic docs-folder detection or an explicit path and package metadata:

```bash
groundkit add ./my-lib --path docs --name my-library --pkg-version 1.0.0
```

For Git sources, pin a tag for reproducible packages:

```bash
groundkit add https://github.com/mattpocock/skills --tag main
```

Build one of the curated sources by name:

```bash
groundkit catalog
groundkit add react
```

## Query it

```bash
groundkit list
groundkit query react "useEffect cleanup"
```

`list` shows installed package name, version, SQLite package size, document count, section count, and a totals summary.

Packages live under `.groundkit/packages` by default. Set `GROUNDKIT_HOME` to move that store.

Inspect package provenance before querying it:

```bash
groundkit inspect my-library
```

## Start MCP

```bash
dotnet run --project src/groundkit-mcp
```

The server uses stdio transport. Add it to your agent configuration using the command and arguments shown in [MCP setup](/guide/mcp).
