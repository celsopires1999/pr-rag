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
- **Skill and draft state live in `AgentSessionState`, not the MAF session.** `SkillSessionState` and `RequisitionDraftSessionState` are helpers over the application-owned `AgentSessionState` (one per client `session_id`, held by `InMemoryAgentSessionStore`), reached from tools via `AgentTurnContext.State`. They used to sit in `AgentSession.StateBag`, which coupled them to the session object's lifetime: MAF binds a session to the agent instance that created it, so a cached session pinned one composition of the agent graph for the whole conversation — and that graph captures request-scoped services in its tool delegates. The `AgentSession` is now created **per turn** and the conversation is replayed into each run, so never reintroduce a cached `AgentSession` or a state-bag read. Answers are never prefixed, and skill persistence does not depend on parsing plain-text history. `AgentSessionState.History` holds **text turns only** — see the workflow-output gotcha below for why no tool traffic may be recorded.
- **A run's messages are not the answer, and no tool traffic is recorded.** `includeWorkflowOutputsInResponse` is `false` on purpose. Left `true`, the workflow folds the whole run back as outputs — the request replayed ahead of the reply — so `response.Text` and the streamed deltas arrive as the conversation's own history in front of the new answer, and every turn after the first looks like a repetition. Two things follow, and both are load-bearing:
  - The answer is the run's `Text`, and `RecordTurn` records `question` + `answer` from it. It does **not** read `response.Messages`. Reassembling the recorded conversation out of those messages is what reintroduced the echo; `Answer_is_the_new_reply_and_not_the_replayed_transcript` guards it.
  - `AgentSessionState.History` never holds a `FunctionCallContent` or a `FunctionResultContent`. Neither half is replayable: across a handoff the specialist makes the call, so the workflow surfaces the result alone, and the framework batches results into one message whose last call is emitted as a *later* assistant message — so no prefix of a tool transcript is well-formed. Replaying either half is rejected by the chat API outright (`messages with role 'tool' must be a response to a preceding message with 'tool_calls'`) and fails the entire turn, not just the replay. The answer already carries the grounded rows.
  - Do not "fix" this by filtering streamed text against the sent messages. A new answer can be byte-identical to an earlier one, so text matching silently eats real output.
- **Every agent gets an inert `ChatHistoryProvider`.** `ExternalHistoryProvider` contributes nothing on purpose: `ChatService` replays `AgentSessionState.History` into the run and records the turn's own text. Left to its default, each agent would keep a second per-turn copy of a history the application already owns.
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

- **Answer hygiene is a live gate, and only partly a test.** The core prompt once mandated a literal `Thought:/Action:/Observation:` trace and then forbade showing it; the model resolved the contradiction by printing it, and on the semantic path that produced real fabricated completions — a turn wrote "Observation: Executed the search" and called no tool at all. The prompt now keeps the deliberation private and names the failure modes, and `AnswerHygienePromptTests` asserts the prompt (a fake model will not narrate, so the behaviour itself is untestable in-process). For the behaviour, run `scripts/live-answer-hygiene.sh [runs]` against the demo API: it fails on narration in the answer, and on an answer claiming a search that the report shows never ran. It also fails on a turn that produced no answer, on a cold turn that never attempted a retrieval, and on a turn with no report to check — without those it reported a completely dead provider as eight clean runs. Two supporting facts make that check possible: `RagQueryReport.RetrievalAttempted` distinguishes *searched and found nothing* from *never searched*, which `UsedNoContextFallback` cannot (both are `RetrievedCount == 0`), and `ToolNames.ReadOnly` names the read tools so a write-only turn is not counted as a retrieval.
- **An empty run is a failure, not a quiet turn.** MAF completes a run whose chat client threw — a rejected key, a provider outage — and surfaces an empty run rather than an exception, logging the cause under `Microsoft.Extensions.AI.LoggingChatClient` and carrying on. Left alone that means the caller gets `200 OK` with `""` and the only evidence is a log line, so a dead provider is indistinguishable from a quiet turn; it was found by a live test returning nothing, not by any test. `ChatService.RequireAnswer` now throws `ChatTurnFailedException` when a run yields no text, `/api/chat` maps it to **502** (upstream, not this app), and the streaming path aborts rather than sending `[DONE]` after an empty body. Two things depend on the ordering: the report is written *before* the throw, so a failed turn is still diagnosable, and the turn is *not* recorded in history, since replaying a question paired with a blank answer poisons every later turn. `UsedNoContextFallback` is likewise gated on an answer existing — it means *the caller was given the fallback*, and keying it on `RetrievedCount == 0` made the outage report a fallback that was never sent. Do not add a "be lenient when the answer is empty" guard: emptiness is the only signal a provider failure leaves.
- **Adding an agent tool**: the capability units in `Services/Agents/Specialists/` are the tool definition sites — `RequisitionSearchSpecialist`, `RequisitionCreationSpecialist`, `SkillActivationSpecialist`. Pick the unit that owns the capability, then make five edits: the handler, its shared wire-name const in `ToolNames` (used by both registration and the report's `Record` call so the two can't drift), the `<ALLOWED_ACTIONS>` bullet in **that unit's `ActionBlock`** (not in `CoreInstructions` — the bullet lives beside the code it describes), the partition assertion in `AgentFrameworkLayeringTests`, and the layering name assertion. Register the handler **method itself**, never a forwarding lambda that calls it — `AIFunctionFactory` builds the parameter schema from the registered delegate's `MethodInfo`, so a lambda silently drops every parameter `[Description]` and the model stops seeing them. `ToolSchemaTests` is the guard for that. Tool descriptions come from the method's `[Description]` alone; don't also pass one to `AIFunctionFactoryOptions`. Because the prompt is composed from the catalog, a tool that is registered but not named in any `ActionBlock` reaches the model undocumented; `SpecialistCatalog` throws at composition if the per-unit lists stop partitioning the registered set.
- **Skill markdown is prompt input, so a wrong tool name in it is a silent bug.** The shipped `data/skills/*.md` is read by the orchestrator, which holds only the write tools plus `activate_skill`. When the retrieval tools moved to their own agent, the create skill still told the agent to validate codes with `search_by_codes`; the model was told to use a tool it was never offered, so the step silently never ran and **no test failed**. Reports from before the split show that call on creation turns, every turn after it shows none. Two rules follow: name only tools the running agent owns, and put any *authoritative* check in the tool's own code rather than in prose — `create_requisition` calls `ExistsItemSupplierCombinationAsync` and refuses, which is why the combination guard survived the split when the model's early warning did not. Routing guidance belongs in the owning unit's `ActionBlock`, never in skill text: the graph decides who reaches whom, and skill text drifts from it without failing anything. `ShippedSkillTextTests` reads the real files (not a fixture) and fails on all three; it was verified to fail when the old instruction is restored. `SkillFrameworkTests` had been seeding the same stale text as a fixture, so a test was passing on guidance that no longer ships.
- **The active skill reaches a participant even though the manifest does not.** `AgentInstructions.ComposeSystemPrompt(skillService.GetManifest(), ...)` is given only to the orchestrator, because only the orchestrator holds `activate_skill` and so is the only agent that can be told what exists. The *activated* guidance is a different channel: `ChatService.BuildTurnMessages` prepends it as a run-level `ChatRole.System` message, and the run's messages reach every agent in the workflow. So a turn that hands off still carries the skill. Assert it with `chatClient.LastMessages`, not `LastPrompt` — and note the guidance is injected on the turn *after* activation, once, so restating it every turn would stop the workflow ending.
- **The agent's system prompt is an `Instructions` value, not a message.** `ChatClientAgent` passes `Instructions` as `ChatOptions.Instructions`; it never enters the message history. Two consequences worth not rediscovering: the prompt is rebuilt per request (so a skill manifest reloaded mid-session reaches the next turn), and it cannot be duplicated across turns. `FakeChatClient` folds `ChatOptions.Instructions` into `LastPrompt` for exactly this reason — without that, prompt assertions read zero and look like the prompt vanished.
- **Phase 2 is a handoff workflow, and `IAgentRunService` is the seam.** It will be built on `Microsoft.Agents.AI.Workflows`, with the workflow presented to callers as an `AIAgent` via `WorkflowHostingExtensions.AsAIAgent(...)` so `ChatService`, the endpoints, streaming, and the session store all keep working unchanged. Do not change `IAgentRunService`'s shape. `SpecialistDefinition.Id` is the seed for the stable agent slug; when the definitions become real agents, `Id` and `Name` must **split** — `Id` is for telemetry correlation and must stay stable across a display rename, so a rename must never touch it.

## Tests

- Integration tests use `TEST_CONNECTION_STRING` env var (set by compose or manually).
- When `TEST_CONNECTION_STRING` is unset, `TestDatabase.ConnectionStringTemplate` falls back to a sensible host: `Host=db` when running inside the DevContainer (detected via `REMOTE_CONTAINERS` env or the presence of `/.dockerenv`/`/workspaces`), otherwise `Host=localhost`. This lets the VS Code test extension run the tests inside the DevContainer with no manual env setup — it connects to the compose `db` service instead of failing on `localhost`.
- Tests run at container runtime (`ENTRYPOINT dotnet test`), not build time — this is because the `db` service isn't available during image build.
- Test fakes: `FakeChatClient`, `FakeEmbeddingService` — no real OpenAI calls during tests. `FakeQueryRewriter` was removed along with `IQueryRewriter` (the model supplies the `search_semantic` query itself).
- `FakeChatClient` scripts tool calls rather than invoking them: add a `FunctionCallContent` to `ScriptedToolCalls` (multi-step) or set `ToolCall` (one-shot), then read the result back with `LastToolResultJson()`. MAF's own loop dispatches the real handler and appends the `FunctionResultContent`.
- Coverage: ingestion diff (initial, no-change, changed/new rows), agentic retrieval and tool schemas, skill framework, RAG observability report, created-requisition listing.
