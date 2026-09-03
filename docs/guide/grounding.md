# Grounding model

Grounding means connecting an agent to external facts at response time. DocsContext applies that idea to versioned technical documentation.

## Grounding pipeline

1. Identify the library and version needed.
2. Resolve an installed package or discover one in the registry.
3. Retrieve focused sections from the local FTS5 index.
4. Return structured hits and concise readable text through MCP.
5. Let the host agent use those sections as evidence for its answer.

## Precision rules

- Prefer short API names and concrete keywords.
- Query the package matching the project version.
- Keep token and hit limits bounded.
- Treat empty results as missing evidence, not permission to invent an answer.
- Inspect source metadata when version or freshness matters.

## What DocsContext does not do

DocsContext does not claim that retrieval alone makes every generated answer correct. The host agent still needs to cite, compare, and verify returned documentation when the decision is consequential.
