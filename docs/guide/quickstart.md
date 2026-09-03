# Quickstart

DocsContext turns documentation into local packages and serves those packages to MCP-compatible AI agents.

## Prerequisites

- .NET SDK 10
- Git, when building from a repository
- An MCP-compatible client such as VS Code, Claude Desktop, or Cursor

## Build a package

Build documentation from a repository:

```bash
dotnet run --project src/DocsContext.AppHost -- add https://github.com/vuejs/docs --docs-path src
```

Build one of the curated sources by name:

```bash
dotnet run --project src/DocsContext.AppHost -- catalog
dotnet run --project src/DocsContext.AppHost -- add react
```

## Query it

```bash
dotnet run --project src/DocsContext.AppHost -- list
dotnet run --project src/DocsContext.AppHost -- query react "useEffect cleanup"
```

Packages live under `.docs-context/packages` by default. Set `DOCSCONTEXT_HOME` to move that store.

## Start MCP

```bash
dotnet run --project src/DocsContext.AppHost -- serve
```

The server uses stdio transport. Add it to your agent configuration using the command and arguments shown in [MCP setup](/guide/mcp).
