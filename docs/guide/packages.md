# Package workflow

## Sources

The builder accepts local directories, Git repositories, `llms.txt` and `llms-full.txt` URLs, and raw documentation pages.

## Portable artifacts

Export a package for another machine:

```bash
dotnet run --project src/DocsContext.AppHost -- export react ./artifacts
```

Import it into the local store:

```bash
dotnet run --project src/DocsContext.AppHost -- import ./artifacts/react@dev.db
```

## Refresh

Refresh from stored source metadata after documentation changes:

```bash
dotnet run --project src/DocsContext.AppHost -- refresh react
```
