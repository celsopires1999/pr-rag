# AGENTS.md

## Verify

```bash
dotnet build PrRag.sln
dotnet test tests/PrRag.Tests
```

Tests require a reachable Postgres instance:

```bash
TEST_CONNECTION_STRING="Host=localhost;Port=5432;Username=prrag;Password=prrag" \
  dotnet test tests/PrRag.Tests
```

Or via Docker (db must already be running):

```bash
docker compose -f docker-compose.yml -f docker-compose.test.yml --profile test up --build test
```

There is no linter, formatter, or typecheck script beyond `dotnet build`.

The Vite + React front-end in `web/` has its own verify:

```bash
cd web && npm install && npm run build
```

## Architecture

Layered .NET 10 solution (`PrRag.sln`):

- **PrRag.Application** — domain models, DTOs, service interfaces, business logic. No infrastructure dependencies.
- **PrRag.Infrastructure** — EF Core DbContext, pgvector, OpenAI clients, file watcher. Implements Application abstractions.
- **PrRag.Api** — ASP.NET minimal API host. Calls `AddApplication()` / `AddInfrastructure()` for DI, runs migrations on startup via `DbInitializer.ApplyMigrationsAsync`.
- **PrRag.Tests** — xUnit integration tests against a real Postgres. Uses fakes for OpenAI (no API key needed).
- **PrRag.DataGenerator** — standalone console tool, outputs `purchase.json`.
- **web/** — Vite + React + TypeScript front-end, not part of the .NET solution. Calls the API endpoints (`/api/chat`, `/api/ingest`, `/api/status`).

Dependency direction: Api -> Infrastructure -> Application.

The API enables cross-origin access from the origins in `Cors__AllowedOrigins` (comma-separated, default `http://localhost:5173`). The `web` service runs under the `demo` profile in `docker-compose.yml`; NPM-driven builds are separate from `dotnet build`.

## Key gotchas

- **`demo` profile**: The API service is behind `docker compose --profile demo`. A plain `docker compose up -d` only starts the database. This is intentional — it prevents the OpenAI key from leaking into `docker compose config` output.
- **Embedding dimension is coupled to model**: `text-embedding-3-small` produces 1536-d vectors. Changing the model requires a new EF Core migration and reindex.
- **`data/purchase.json`** is a bind-mount volume. The API watches it for changes (FileSystemWatcher + debounce). The file is read-only inside the API container.
- **Settings use `__` separator** in `.env` (e.g. `OpenAI__ApiKey`) — these are the same `.NET` config keys the app reads. No duplication.
- **EF Core migrations auto-apply on API startup.** No manual step needed. To create explicit migrations: `dotnet ef migrations add <Name> --project src/PrRag.Infrastructure/PrRag.Infrastructure.csproj`.
- **CORS is config-driven**: `Cors__AllowedOrigins` (default `http://localhost:5173`) controls what origins may call the API from the browser. Add origins (comma-separated) if the front-end is served elsewhere.
- **DevContainer build owner**: The `devcontainer` service runs as `root` by default, but `devcontainer.json` sets `"remoteUser": "vscode"`. Running `dotnet build` as `root` inside the container writes `obj/`/`bin/` artifacts owned by `root`; a subsequent VS Code build (as `vscode`) fails with `Permission denied` writing `.cache` files. Always build as `vscode` (e.g. `docker compose exec -u vscode devcontainer dotnet build ...` or the VS Code `build` task). If a root-owned build breaks things, remove all `bin`/`obj` as root first, then rebuild as `vscode`:
  ```sh
  docker compose exec -u root devcontainer sh -c 'find /workspaces/src /workspaces/tests /workspaces/tools -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +'
  docker compose exec -u vscode -w /workspaces devcontainer dotnet build /workspaces/PrRag.sln -c Debug
  ```

## Tests

- Integration tests use `TEST_CONNECTION_STRING` env var (set by compose or manually).
- Tests run at container runtime (`ENTRYPOINT dotnet test`), not build time — this is because the `db` service isn't available during image build.
- Test fakes: `FakeChatClient`, `FakeEmbeddingService`, `FakeQueryRewriter` — no real OpenAI calls during tests.
- Coverage: ingestion diff (initial, no-change, changed/new rows), query rewriter retrieval, RAG observability report.
