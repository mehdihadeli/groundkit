# Architecture

GroundKit separates expensive ingestion from the fast retrieval path.

```mermaid
flowchart LR
  A[Git, folder, URL, llms.txt] --> B[Ingestion]
  B --> C[SQLite package]
  C --> D[FTS5 and BM25]
  D --> E[MCP tools]
  E --> F[AI agent]
```

## Build phase

The ingestion layer detects the source, loads supported documents, extracts sections, estimates tokens, computes fingerprints, and creates a `BuildResult`.

The storage layer persists manifests, source metadata, documents, chunks, warnings, and an FTS5 search table in one portable `.db` file.

## Query phase

The MCP server opens the selected package, builds an FTS query, ranks matches with BM25, applies a relative relevance cutoff, and trims results to the requested token budget.

Queries do not fetch the internet. Registry access is only needed when discovering or downloading a package.

The result includes package identity and version, selected document and section titles, content, token estimates, code presence, and relevance scores. This makes retrieved evidence inspectable by both an MCP client and a human reviewing an agent response.

## Data boundaries

GroundKit is a static documentation grounding layer. It is appropriate for versioned API references, guides, runbooks, and other documents that can be packaged. It is not a source of live operational truth: prices, inventory, feature flags, and service status belong behind a current API or database query.

## Why SQLite

Library documentation is curated, structured, and queried with concrete API names. Full-text search gives inspectable exact-term matching without embedding generation, vector infrastructure, or a remote query dependency. A vector index can be added for a corpus with large scale, weak vocabulary overlap, or unstructured content, but it is not required by this documentation use case.
