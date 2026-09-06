# AGENTS.md

## Verify

```bash
dotnet build backend/PrRag.sln
dotnet test backend/tests/PrRag.Tests
```

Tests require a reachable Postgres instance:

```bash
TEST_CONNECTION_STRING="Host=localhost;Port=5432;Username=prrag;Password=prrag" \
  dotnet test backend/tests/PrRag.Tests
```

Or via Docker (db must already be running):

```bash
docker compose -f docker-compose.yml -f docker-compose.test.yml --profile test up --build test
```

There is no linter, formatter, or typecheck script beyond `dotnet build`.

The Vite + React front-end in `frontend/` has its own verify:

```bash
cd frontend && npm install && npm run build
```

## Architecture

Layered .NET 10 solution (`backend/PrRag.sln`):

- **PrRag.Application** — domain models, DTOs, service interfaces, business logic. No infrastructure dependencies.
- **PrRag.Infrastructure** — EF Core DbContext, pgvector, OpenAI clients, file watcher. Implements Application abstractions.
- **PrRag.Api** — ASP.NET minimal API host. Calls `AddApplication()` / `AddInfrastructure()` for DI, runs migrations on startup via `DbInitializer.ApplyMigrationsAsync`.
- **PrRag.Tests** — xUnit integration tests against a real Postgres. Uses fakes for OpenAI (no API key needed).
- **PrRag.DataGenerator** — standalone console tool, outputs `purchase.json`.
- **`web`/`frontend` layout** — the .NET solution and projects live in `backend/` (together with the Dockerfiles); the Vite + React + TypeScript front-end lives in `frontend/`, not part of the .NET solution. It calls the API endpoints (`/api/chat`, `/api/ingest`, `/api/status`). Compose files, DevContainer config, and docs stay at the repo root.

Dependency direction: Api -> Infrastructure -> Application.

The API enables cross-origin access from the origins in `Cors__AllowedOrigins` (comma-separated, default `http://localhost:5173`). The `web` service runs under the `demo` profile in `docker-compose.yml`; NPM-driven builds are separate from `dotnet build`.

## Key gotchas

- **`demo` profile**: The API service is behind `docker compose --profile demo`. A plain `docker compose up -d` only starts the database. This is intentional — it prevents the OpenAI key from leaking into `docker compose config` output.
- **Writable mount `./reports`**: the API image runs as the non-root `app` user (UID 1654). On a fresh start Docker may create the bind-mount source dir owned by `root`, which causes 500s when the observability report writes. The runtime entrypoint starts as root, chowns the dir to `app:app`, and then drops privileges.
- **Skill markers persist across turns**: while a skill is active the returned chat answer is prefixed `[Skill: <skill-name>]` (enforced by `ChatService.ApplySkillMarker`, not just prompt instructions) so the next request's plain-text history can restore the guidance; the marker is dropped once a requisition is created.
- **Embedding dimension is coupled to model**: `text-embedding-3-small` produces 1536-d vectors. Changing the model requires a new EF Core migration and reindex.
- **`data/purchase.json`** is a bind-mount volume. The API watches it for changes (FileSystemWatcher + debounce). The file is read-only inside the API container.
- **Settings use `__` separator** in `.env` (e.g. `OpenAI__ApiKey`) — these are the same `.NET` config keys the app reads. No duplication.
- **EF Core migrations auto-apply on API startup.** No manual step needed. To create explicit migrations: `dotnet ef migrations add <Name> --project src/PrRag.Infrastructure/PrRag.Infrastructure.csproj` (from `backend/`).
- **CORS is config-driven**: `Cors__AllowedOrigins` (default `http://localhost:5173`) controls what origins may call the API from the browser. Add origins (comma-separated) if the front-end is served elsewhere.
- **DevContainer build owner**: The `devcontainer` service runs as `root` by default, but `devcontainer.json` sets `"remoteUser": "vscode"`. Running `dotnet build` as `root` inside the container writes `obj/`/`bin/` artifacts owned by `root`; a subsequent VS Code build (as `vscode`) fails with `Permission denied` writing `.cache` files. Always build as `vscode` (e.g. `docker compose exec -u vscode devcontainer dotnet build ...` or the VS Code `build` task). If a root-owned build breaks things, remove all `bin`/`obj` as root first, then rebuild as `vscode`:
  ```sh
  docker compose exec -u root devcontainer sh -c 'find /workspaces/backend/src /workspaces/backend/tests /workspaces/backend/tools -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +'
  docker compose exec -u vscode -w /workspaces/backend devcontainer dotnet build /workspaces/backend/PrRag.sln -c Debug
  ```

## Tests

- Integration tests use `TEST_CONNECTION_STRING` env var (set by compose or manually).
- When `TEST_CONNECTION_STRING` is unset, `TestDatabase.ConnectionStringTemplate` falls back to a sensible host: `Host=db` when running inside the DevContainer (detected via `REMOTE_CONTAINERS` env or the presence of `/.dockerenv`/`/workspaces`), otherwise `Host=localhost`. This lets the VS Code test extension run the tests inside the DevContainer with no manual env setup — it connects to the compose `db` service instead of failing on `localhost`.
- Tests run at container runtime (`ENTRYPOINT dotnet test`), not build time — this is because the `db` service isn't available during image build.
- Test fakes: `FakeChatClient`, `FakeEmbeddingService`, `FakeQueryRewriter` — no real OpenAI calls during tests.
- Coverage: ingestion diff (initial, no-change, changed/new rows), query rewriter retrieval, RAG observability report.
