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
- **Skill state lives in the session state bag**: `SkillSessionState` (a helper over `AgentSession.StateBag`) owns the active skill id/body keys, activation, and per-turn re-injection. The old `[Skill: <skill-name>]` answer-prefix marker was removed in the MAF migration — answers are never prefixed, and skill persistence does not depend on parsing plain-text history.
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

- **Adding an agent tool**: the capability units in `Services/Agents/Specialists/` are the tool definition sites — `RequisitionSearchSpecialist`, `RequisitionCreationSpecialist`, `SkillActivationSpecialist`. Pick the unit that owns the capability, then make five edits: the handler, its shared wire-name const in `ToolNames` (used by both registration and the report's `Record` call so the two can't drift), the `<ALLOWED_ACTIONS>` bullet in **that unit's `ActionBlock`** (not in `CoreInstructions` — the bullet lives beside the code it describes), the partition assertion in `AgentFrameworkLayeringTests`, and the layering name assertion. Register the handler **method itself**, never a forwarding lambda that calls it — `AIFunctionFactory` builds the parameter schema from the registered delegate's `MethodInfo`, so a lambda silently drops every parameter `[Description]` and the model stops seeing them. `ToolSchemaTests` is the guard for that. Tool descriptions come from the method's `[Description]` alone; don't also pass one to `AIFunctionFactoryOptions`. Because the prompt is composed from the catalog, a tool that is registered but not named in any `ActionBlock` reaches the model undocumented; `SpecialistCatalog` throws at composition if the per-unit lists stop partitioning the registered set.
- **The agent's system prompt is an `Instructions` value, not a message.** `ChatClientAgent` passes `Instructions` as `ChatOptions.Instructions`; it never enters the message history. Two consequences worth not rediscovering: the prompt is rebuilt per request (so a skill manifest reloaded mid-session reaches the next turn), and it cannot be duplicated across turns. `FakeChatClient` folds `ChatOptions.Instructions` into `LastPrompt` for exactly this reason — without that, prompt assertions read zero and look like the prompt vanished.
- **Phase 2 is a handoff workflow, and `IAgentRunService` is the seam.** It will be built on `Microsoft.Agents.AI.Workflows`, with the workflow presented to callers as an `AIAgent` via `WorkflowHostingExtensions.AsAIAgent(...)` so `ChatService`, the endpoints, streaming, and the session store all keep working unchanged. Do not change `IAgentRunService`'s shape. `SpecialistDefinition.Id` is the seed for the stable agent slug; when the definitions become real agents, `Id` and `Name` must **split** — `Id` is for telemetry correlation and must stay stable across a display rename, so a rename must never touch it.

## Tests

- Integration tests use `TEST_CONNECTION_STRING` env var (set by compose or manually).
- When `TEST_CONNECTION_STRING` is unset, `TestDatabase.ConnectionStringTemplate` falls back to a sensible host: `Host=db` when running inside the DevContainer (detected via `REMOTE_CONTAINERS` env or the presence of `/.dockerenv`/`/workspaces`), otherwise `Host=localhost`. This lets the VS Code test extension run the tests inside the DevContainer with no manual env setup — it connects to the compose `db` service instead of failing on `localhost`.
- Tests run at container runtime (`ENTRYPOINT dotnet test`), not build time — this is because the `db` service isn't available during image build.
- Test fakes: `FakeChatClient`, `FakeEmbeddingService` — no real OpenAI calls during tests. `FakeQueryRewriter` was removed along with `IQueryRewriter` (the model supplies the `search_semantic` query itself).
- `FakeChatClient` scripts tool calls rather than invoking them: add a `FunctionCallContent` to `ScriptedToolCalls` (multi-step) or set `ToolCall` (one-shot), then read the result back with `LastToolResultJson()`. MAF's own loop dispatches the real handler and appends the `FunctionResultContent`.
- Coverage: ingestion diff (initial, no-change, changed/new rows), agentic retrieval and tool schemas, skill framework, RAG observability report, created-requisition listing.
