# Package workflow

Packages are versioned evidence artifacts. Build or import the package that matches the dependency version used by the project; do not combine multiple versions in one lookup workflow without making that choice explicit.

The repository also includes a declarative starter registry in `registry/`. Its YAML files describe source locations for the curated catalog. The current CLI still builds from a source URL or catalog entry; YAML discovery and registry publishing are future work.

## Sources

The builder accepts local directories, Git repositories, `llms.txt` and `llms-full.txt` URLs, and raw documentation pages.

### Local directories

When the source is a local directory, GroundKit checks these folders in order:

1. `docs/`
2. `documentation/`
3. `doc/`

If none exists, it scans the repository root. Set `--path` when documentation lives elsewhere:

```bash
groundkit add ./my-library
groundkit add ./my-library --path handbook
```

`--docs-path` remains available as an alias for `--path`. Relative paths resolve from the source directory; absolute paths are also supported.

### Git repositories

GroundKit accepts GitHub, GitLab, Bitbucket, and Codeberg repository URLs. GitHub tree URLs preserve their selected ref, and `--tag` explicitly selects a tag or branch:

```bash
groundkit add https://github.com/mattpocock/skills --tag main
```

Without an explicit ref, GroundKit chooses the latest stable tag when the remote exposes tags. Add `--choose-tag` for interactive selection.

### Package identity

By default, package ID and display name come from the source name. Override them and attach a version when publishing or maintaining multiple builds from one source:

```bash
groundkit add ./my-library --name my-library --pkg-version 1.0.0
```

The custom name is used for package and source identity; the custom version is stored in both package and source metadata and is shown by `inspect`.

Save a portable copy while also installing the package locally:

```bash
groundkit add ./my-library --path docs --name my-library --pkg-version 1.0.0 --save ./artifacts
```

`--save` accepts either a destination directory or a `.db` file path. A
directory receives the normal package filename, making the copy ready for
sharing or importing on another machine.

During ingestion, GroundKit records source kind, canonical source ID, location, optional version, tag or branch, fingerprint, build timestamps, document counts, chunk counts, and warnings. Use `inspect` to review this provenance.

## Portable artifacts

Export a package for another machine:

```bash
dotnet run --project src/groundkit -- export react ./artifacts
```

Import it into the local store:

```bash
dotnet run --project src/groundkit -- import ./artifacts/react@dev.db
```

## Refresh

Refresh from stored source metadata after documentation changes:

```bash
dotnet run --project src/groundkit -- refresh react
```

Refresh rebuilds the package from its stored source metadata. It does not make a local query live or guarantee that an upstream branch has not changed. For reproducible grounding, prefer an exact Git tag or dependency version, then export the resulting `.db` artifact for teammates or CI.

## Evidence workflow

```bash
dotnet run --project src/groundkit -- inspect react
dotnet run --project src/groundkit -- query react "useEffect cleanup"
```

Check package identity, version, source, and build time before treating a result as authoritative. An empty query result means the package did not provide evidence for that query; it does not prove that the documented feature does not exist.
