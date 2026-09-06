# Community Registry

Registry definitions describe where documentation comes from and how to build a package. Each YAML file is an unversioned source definition for the DocsContext package pipeline.

The current .NET application consumes published SQLite packages through the compatible HTTP registry API. The scheduled [Registry Update workflow](../.github/workflows/registry-update.yml) validates these definitions, rebuilds their SQLite packages, and uploads them as workflow artifacts. It does not publish to a hosted registry yet because this repository has no write-side registry API.

## Layout

Directory names identify package registries. File names identify package names within a registry:

```text
registry/npm/react.yaml -> npm/react
registry/pip/fastapi.yaml -> pip/fastapi
```

Scoped npm packages use a subdirectory:

```text
registry/npm/@trpc/server.yaml -> npm/@trpc/server
```

The `name` field must match the package name represented by its path.

## Definition format

Unversioned definition:

```yaml
name: react
description: "User interface library"
repository: https://github.com/reactjs/react.dev

source:
  type: git
  url: https://github.com/reactjs/react.dev
  docs_path: src/content
```

Supported source types currently represented by this registry are `git`, local directory, `llms.txt`, and raw page. Documentation inputs should be Markdown, MDX, HTML, AsciiDoc, or reStructuredText where supported by the ingestion pipeline.

## Versioned definitions

Use `versions` instead of `source` when a package must be built for exact releases. Do not combine both shapes:

```yaml
name: example
description: "Example library"
repository: https://github.com/example/example

versions:
  - min_version: "1.0.0"
    tag_pattern: "v{version}"
    source:
      type: git
      url: https://github.com/example/example
      docs_path: docs
```

Version discovery and hosted registry publishing are not implemented in this repository yet. Until then, build exact-version packages with the CLI and import or export the resulting SQLite artifact. The current workflow supports the unversioned Git definitions in this directory.

## Add a definition

1. Choose the distribution registry directory, such as `npm`.
2. Add `<name>.yaml` and keep its `name` consistent with the path.
3. Point `docs_path` at the directory containing documentation.
4. Build the source with the CLI and inspect the result:

```bash
dotnet run --project src/DocsContext.AppHost -- add <repository-url> --docs-path <path>
dotnet run --project src/DocsContext.AppHost -- inspect <package-id>
```

A registry builder should reject malformed definitions, missing documentation roots, and definitions that produce empty or suspiciously small packages. CI currently validates required fields and builds every definition; package health thresholds remain future work.

## Included definitions

The initial definitions mirror the curated catalog in `src/DocsContext.Core/Contracts/LibraryCatalog.cs`. They are intentionally simple Git sources so each entry can be reviewed and extended independently.
