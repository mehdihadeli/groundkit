# CLI reference

Run commands with `dotnet run --project src/groundkit -- <command>`. After publishing the CLI, replace that prefix with `groundkit`.

## Add sources

`add` accepts a local directory, Git repository URL, `llms.txt` URL, or raw documentation URL.

| Option                    | Purpose                                                                                                      |
| ------------------------- | ------------------------------------------------------------------------------------------------------------ |
| `--path <path>`           | Documentation directory inside a local directory or repository. Relative paths resolve from the source root. |
| `--docs-path <path>`      | Backward-compatible alias for `--path`.                                                                      |
| `--name <name>`           | Override generated package ID and display name.                                                              |
| `--pkg-version <version>` | Set package and source version metadata.                                                                     |
| `--tag <tag>`             | Build a Git repository at a specific tag or ref.                                                             |
| `--choose-tag`            | Interactively choose a Git tag when the terminal supports prompts.                                           |

Examples:

```bash
# Detect docs/, documentation/, or doc/ automatically
groundkit add ./my-library

# Use an explicit documentation directory
groundkit add /path/to/repo --path docs

# Set package identity and version
groundkit add ./my-library --name my-library --pkg-version 1.0.0

# Build a repository at a tag
groundkit add https://github.com/mattpocock/skills --tag v1.0.0
```

For Git repository URLs without `--tag`, GroundKit selects the latest stable tag when tags are available. `--choose-tag` changes this to an interactive selection.

| Command                                                                  | Purpose                                                                                         |
| ------------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------- |
| `add <source> [--path <path>] [--name <name>] [--pkg-version <version>]` | Build and install a package; local directories auto-detect `docs/`, `documentation/`, or `doc/` |
| `catalog [query]`                                                        | Browse curated sources                                                                          |
| `list`                                                                   | List installed packages                                                                         |
| `query <package-id> <topic>`                                             | Search local documentation                                                                      |
| `inspect <package-id>`                                                   | Inspect package and source metadata                                                             |
| `refresh <package-id>`                                                   | Rebuild from stored source metadata                                                             |
| `import <package-file>`                                                  | Install a SQLite package                                                                        |
| `export <package-id> <destination>`                                      | Copy a package artifact                                                                         |
| `search-packages <registry> <name> [version]`                            | Search hosted registry                                                                          |
| `download-package <registry> <name> <version>`                           | Download and install package                                                                    |
| `serve`                                                                  | Start the stdio MCP server                                                                      |
| `remove <package-id>`                                                    | Remove installed package                                                                        |

`install <registry/name|name|source> [version]` resolves a package from the configured registry or catalog, downloads/builds it, and installs it locally. Use `search-packages` to inspect registry versions first.

The registry endpoint defaults to `http://localhost:8080`. Override it with `GROUNDKIT_REGISTRY_URL`.
