# Registry API

DocsContext accepts a small compatible HTTP registry API.

## Search

```http
GET /search?registry=npm&name=react&version=19.1.0
```

Returns package entries containing `name`, `registry`, `version`, `description`, and optional `size`.

## Download

```http
GET /packages/npm/react/19.1.0/download
```

Returns the raw SQLite package as `application/octet-stream`.

## Package metadata

```http
GET /packages/npm/react/19.1.0
```

Returns package identity and optional source commit information.

The server may be local, internal, or hosted. Configure its base URL with `DOCSCONTEXT_REGISTRY_URL`.
