# GroundKit Registry CLI

`GroundKit.Registry` is the maintainer and CI tool for AI-ready documentation packages. It is separate from the user-facing `groundkit` MCP server.

## Lifecycle

1. Add one YAML definition under `registry/packages/`.
2. Validate definitions.
3. Build packages from Git sources at `latest`, a branch, or an exact tag.
4. Publish packages to a compatible registry API.
5. MCP clients search and download packages, then query them locally.

## Install

Run the maintainer CLI with `groundkit-registry <command>`.

Run `groundkit-registry --help` to see the same aliases, descriptions, and
examples in the built-in CLI help.

Short aliases are also available through Spectre.Console.Cli.

| Command         | Short alias(es) |
| --------------- | --------------- |
| `list`          | `l`, `ls`       |
| `validate`      | `v`, `val`      |
| `build`         | `b`             |
| `build-all`     | `ba`            |
| `publish`       | `p`, `pub`      |
| `publish-all`   | `pa`            |
| `bundle`        | `bd`, `bun`     |
| `import-bundle` | `ib`            |
| `serve`         | `s`             |

| Option          | Short alias |
| --------------- | ----------- |
| `--dir`         | `-d`        |
| `--output`      | `-o`        |
| `--format`      | `-f`        |
| `--destination` | `-t`        |
| `--urls`        | `-u`        |

During development, this repository includes repo-local `groundkit-registry`
launchers in the repository root. Add the repository root to `PATH` once per
shell session, then use the same command shown below:

```bash
export PATH="$PWD/../..:$PATH"
groundkit-registry list --dir registry/packages
```

```powershell
$env:Path = "$(Resolve-Path ../..);$env:Path"
groundkit-registry list --dir registry/packages
```

Install the published maintainer tool from NuGet:

```powershell
dotnet tool install --global GroundKit.Registry
groundkit-registry list
```

For a locally built package:

```powershell
dotnet pack src/GroundKit.Registry -c Release
dotnet tool install --global --add-source ./src/GroundKit.Registry/bin/Release GroundKit.Registry
```

## Commands

```bash
groundkit-registry list --dir registry/packages
groundkit-registry validate --dir registry/packages
groundkit-registry build react --dir registry/packages --output ./dist-packages
groundkit-registry build react 19.1.0 --dir registry/packages --output ./dist-packages
groundkit-registry publish react --dir registry/packages --output ./dist-packages
groundkit-registry publish-all --dir registry/packages --output ./dist-packages
groundkit-registry bundle --output ./dist-packages --format zip
groundkit-registry bundle --output ./dist-packages --format tar.gz
groundkit-registry import-bundle ./dist-packages/groundkit-registry.zip --output ./imported-packages
```

`publish` skips an existing `registry/name/version`. `publish-all` processes every definition and version, continues after individual failures, and exits nonzero when any package fails.

Bundles contain all individual packages plus `index.json` and `SHA256SUMS`. GitHub Actions publishes both individual `.db` artifacts and ZIP/tar.gz bundles from the same build output.

## Local registry API

Start the API and MinIO with Docker Compose:

```bash
cp .env.example .env
docker compose up --build
```

The API is available at `http://localhost:8080`; MinIO is available at `http://localhost:9001`. Set `REGISTRY_SERVER_URL` and `REGISTRY_PUBLISH_KEY` when publishing. Individual packages can also be mirrored to GHCR as OCI artifacts by the GitHub Actions workflow.

## Definition format

Unversioned source:

```yaml
name: react
description: "User interface library"
repository: https://github.com/reactjs/react.dev
source:
  type: git
  url: https://github.com/reactjs/react.dev
  docs_path: src/content
```

Versioned Git source:

```yaml
name: example
description: "Example library"
source:
  type: git
  url: https://github.com/example/example
versions:
  - version: 1.0.0
    tag: v1.0.0
  - version: 2.0.0
    tag: v2.0.0
```

The YAML name must match its filename. All definitions use the `packages` catalog. Keep source definitions deterministic: pin tags for released packages and use `latest` only for sources without release-aligned documentation.

## Publishing target

Do not publish SQLite packages to npm. npm is a package manager for JavaScript packages, and GitHub Container Registry is for OCI images; neither matches GroundKit's HTTP package API.

Recommended production options:

- A small object-storage-backed registry service, such as S3, Azure Blob Storage, or Cloudflare R2, exposing the GroundKit API.
- A dedicated HTTP service backed by object storage and a metadata database for search, versions, authentication, and immutable uploads.
- GitHub Releases or an artifact store for simple distribution when search can be generated from repository metadata.

GitHub Container Registry is appropriate only when shipping the registry server itself as a container. npm is appropriate only for distributing the CLI or SDK, not `.db` documentation packages.

Configure publishing with:

```text
REGISTRY_SERVER_URL=https://registry.example.com
REGISTRY_PUBLISH_KEY=<write-only CI secret>
```

The server must implement:

```text
GET  /packages/{registry}/{name}/{version}
POST /packages/{registry}/{name}/{version}
GET  /search?registry={registry}&name={name}&version={version}
GET  /packages/{registry}/{name}/{version}/download
```

Use immutable version keys, checksum verification, authentication on `POST`, and retention/version metadata. Store the publish key only in CI secrets.
