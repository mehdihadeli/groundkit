# Quickstart

GroundKit turns documentation into local packages and serves those packages to MCP-compatible AI agents.

## Prerequisites

- .NET SDK 10
- Git, when building from a repository
- An MCP-compatible client such as VS Code, Claude Desktop, or Cursor

## Build a package

Build documentation from a repository:

```bash
dotnet run --project src/groundkit-cli -- add https://github.com/vuejs/docs --docs-path src
```

Local repositories can use automatic docs-folder detection or an explicit path and package metadata:

```bash
dotnet run --project src/groundkit-cli -- add ./my-lib --path docs --name my-library --pkg-version 1.0.0
```

For Git sources, pin a tag for reproducible packages:

```bash
dotnet run --project src/groundkit-cli -- add https://github.com/mattpocock/skills --tag main
```

Build one of the curated sources by name:

```bash
dotnet run --project src/groundkit-cli -- catalog
dotnet run --project src/groundkit-cli -- add react
```

## Query it

```bash
dotnet run --project src/groundkit-cli -- list
dotnet run --project src/groundkit-cli -- query react "useEffect cleanup"
```

Packages live under `.groundkit/packages` by default. Set `GROUNDKIT_HOME` to move that store.

Inspect package provenance before querying it:

```bash
dotnet run --project src/groundkit-cli -- inspect my-library
```

## Start MCP

```bash
dotnet run --project src/groundkit-mcp
```

The server uses stdio transport. Add it to your agent configuration using the command and arguments shown in [MCP setup](/guide/mcp).
