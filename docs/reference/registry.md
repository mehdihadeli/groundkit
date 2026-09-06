# Registry API

GroundKit accepts a small compatible HTTP registry API.

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

The server may be local, internal, or hosted. Configure its base URL with `GROUNDKIT_REGISTRY_URL`.

## Local server

Start the Compose deployment from the repository root:

```bash
cp .env.example .env
docker compose up --build
```

The API listens on `http://localhost:8080` and MinIO listens on `http://localhost:9001`. Set `REGISTRY_PUBLISH_KEY` in `.env` for authenticated package uploads.
