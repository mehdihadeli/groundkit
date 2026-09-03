# CLI reference

| Command                                        | Purpose                             |
| ---------------------------------------------- | ----------------------------------- |
| `add <source> [--docs-path <path>]`            | Build and install a package         |
| `catalog [query]`                              | Browse curated sources              |
| `list`                                         | List installed packages             |
| `query <package-id> <topic>`                   | Search local documentation          |
| `inspect <package-id>`                         | Inspect package and source metadata |
| `refresh <package-id>`                         | Rebuild from stored source metadata |
| `import <package-file>`                        | Install a SQLite package            |
| `export <package-id> <destination>`            | Copy a package artifact             |
| `search-packages <registry> <name> [version]`  | Search hosted registry              |
| `download-package <registry> <name> <version>` | Download and install package        |
| `serve`                                        | Start the stdio MCP server          |
| `remove <package-id>`                          | Remove installed package            |

The registry endpoint defaults to `http://localhost:8080`. Override it with `DOCSCONTEXT_REGISTRY_URL`.
