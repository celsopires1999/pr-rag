## Context

`ChatService` runs an agentic ReAct loop: the chat model can call two retrieval tools (`search_by_codes`, `search_semantic`) exposed via the Microsoft.Extensions.AI (`IChatClient`) abstraction, then answer grounded in the results. The system prompt is a single constant embedded in `ChatService`. Content on disk is already a first-class pattern: `data/purchase.json` is bind-mounted read-only, loaded by `PurchaseRequisitionFileLoader`, and watched by a `FileSystemWatcher`-based `FileWatcherService` with a 5s debounce. Settings are bound from environment via `IOptions<T>` sections (`Data`, `RAG`, `OpenAI`, `Report`). Reports are written to the `reports/` volume via `IRagReportWriter`.

We want to extend this so recurring workflows (e.g. "create a purchase requisition") are guided by reusable, extensible **skills**: markdown content that tells the model how to drive the conversation for that task.

## Goals / Non-Goals

**Goals:**
- Skills as file-based, versioned markdown content (YAML front-matter + guidance body) — adding a new skill is dropping a file, no code/schema change.
- Chat model detects intent matching a skill (via a manifest) and activates it using the existing tool-calling mechanism.
- Once active, the skill's instructions steer the conversation across turns (draft purchase-requisition, validation, final deliverable).
- Skill usage is recorded in the existing RAG observability report.
- Zero behavior change when no skill matches or no skill directory is present.

**Non-Goals:**
- No skills authored from the API/frontend (files only).
- No new external dependencies.
- Skills do NOT grant the model new capabilities/tools — activation only adds instructions to guide conversation; the tool set is fixed at what the framework registers (retrieval + `activate_skill` + `create_requisition`).
- No DB schema changes or migrations.
- No per-skill custom frontend UI in this change.
- No skill-authoring/versioning tooling (a later change).

## Decisions

### Decision 1 — Skills are markdown files with YAML front-matter (id, description trigger, version) + a guidance body

Each skill is one `*.md` file under a configured directory (default `/data/skills`):

```markdown
---
name: create-purchase-requisition
description: Use this skill when the user wants to CREATE a new purchase requisition, draft one, or ask how to register an item/supplier on a new requisition.
version: 1
---
# Role
You act as a purchasing assistant helping the user draft a new purchase requisition...

# Procedure
1. Gather the required fields one question at a time: item code, quantity, unit of measure, supplier, expected delivery date, requester, and justification. Skip anything the user already provided...
2. Validate referenced item (`ITM-*`) and supplier (`SUP*`) codes with the `search_by_codes` tool and flag any code that returns no match...
3. Once all fields are collected and validated, produce the draft requisition as a structured summary (item, qty, unit, supplier, delivery date, requester, justification) and ask the user to confirm.
4. After the user confirms, call the `create_requisition` tool with the six fields (`SupplierCode`, `Item`, `Description`, `Quantity`, `Date`, `Requester`) and report the file that was created.
```

- Rationale: markdown is human-reviewable, diffable, and directly injectable into the model context. Front matter gives structured metadata for the manifest without a second file format.
- Alternative considered: skills as JSON/C#-compiled classes — rejected: requires a build/deploy cycle per skill, defeats extensibility.

### Decision 2 — Manifest + `activate_skill` tool, reusing the existing agentic loop

The base system prompt is extended with a **Skills section** listing the manifest of every loaded skill (`name: description`). An `activate_skill(name)` tool is registered alongside the existing retrieval tools and the requisition-creation tool (Decision 7). When the model decides the request matches a skill, it calls `activate_skill`, which returns the skill's guidance body as the function result. The body becomes conversation context and the model follows it, asking for the data it needs and calling the existing retrieval tools to validate codes.

- Rationale: mirrors the proven ReAct pattern already in `ChatService` (`ResolveContextAsync`), reuses tool plumbing (`RegisterFunction`/`InvokeFunctionAsync`), needs no separate embedding/classifier pass, and keeps "decide whether/what" firmly on the model like `agentic-retrieval`.
- Alternative considered A (separate classifier that pre-selects a skill): rejected — extra latency and another model call; the chat model already reasons about intent with conversation context.
- Alternative considered B (keyword/globbing trigger matching): rejected — brittle for multi-language, paraphrased requests.

### Decision 3 — Skills are stateless but conversation-aware: re-injected from history on each turn

Because chat is stateless (history is passed per request via `Messages`), skill activation must survive across turns. The history the API receives is plain `ChatMessageDto` text — it cannot carry `FunctionCallContent`. Instead, the system prompt instructs the model that, when a skill is active, its reply must begin with a marker line of the form `[Skill: <skill-name>]`; `ChatService` also **enforces** the marker deterministically in `ApplySkillMarker` — if the model's answer does not already start with one while a skill is active, the prefix is added before the answer is returned to the client (and the marker is dropped once the skill's deliverable — e.g. `requisition-*.json` — has been persisted, so a finished workflow does not keep restoring guidance). In `BuildConversation`, after the base system prompt, `ChatService` scans the incoming history for the most recent assistant message containing that marker (regex `^\[Skill:\s*([\w-]+)\]`), loads the skill, and appends its instructions as an extra **system** message before the user's latest turn. Within the current request, activation also happens live via the tool result during `ResolveContextAsync`.

- Rationale: full fidelity with history lost mid-turn is fine (model can re-activate or ask). Keeps the API contract unchanged (`POST /api/chat`, `/api/chat/stream`) and works with the plain-text history DTOs; enforcing the marker in `ChatService` (instead of trusting the model to emit it) makes cross-turn restoration reliable in practice.
- Alternative considered (scanning history for a `FunctionCallContent` named `activate_skill`): rejected — the front end only sends text messages, so prior tool calls are not present in the history to scan.
- Trade-off acknowledged: the marker line is part of the assistant message text and therefore visible in the chat bubble; guidance depends on the conversation history the client sends; if the client truncates history the step context may thin out.

### Decision 4 — Skill store: singleton loader + directory watcher, mirroring `FileWatcherService`

`ISkillService` (Application) is implemented in Infrastructure by a `SkillsDirectorySource` that:
- scans the configured directory for `*.md` files and parses front matter at startup (and on demand via `ReloadAsync`), building an in-memory manifest + body map;
- registers a debounced `FileSystemWatcher` reload so new/edited skill files are picked up in dev (the runtime volume is `:ro`, so production changes still deploy as content);
- returns the manifest (for the system prompt) and the body for a given skill id.

DI: `ISkillService` registered as a singleton (like `FileRagReportWriter`), because the manifest/body map is immutable after each reload and chats run per-request. `SkillsSettings` (`Skills__Directory`, default `/data/skills`) follows the existing `IOptions<DataSettings>` pattern; the devcontainer and compose set the directory explicitly.

- Rationale: reuses the established watcher debounce pattern and keeps the skills content colocated with `purchase.json` under `data/`, mounted read-only at runtime.
- Alternative considered: reload on every chat request — rejected: wasteful and unnecessary for an add-only content directory.

### Decision 5 — Skill activation is recorded in the existing RAG report

`RagQueryReport` gains `SkillId`, `SkillName`, and `SkillActivated` fields. `ChatService` sets them when `activate_skill` is invoked (or when history re-injection restores an active skill). `FileRagReportWriter` serializes them automatically (new optional JSON fields).

- Rationale: observability stays a single pipeline with zero extra infra; aligns with `rag-observability-report`.

### Decision 6 — Security posture: skills carry instructions, never capabilities

The tool set is fixed at what the framework registers (`search_by_codes`, `search_semantic`, `activate_skill`, `create_requisition`) and is unchanged by which skill, if any, is active. A skill can only tell the model how to converse and which of the always-available tools to use. The system prompt guardrails ("never hallucinate", language matching, code-search constraints) still apply to skill-guided turns. Skills are plain files in a read-only volume, reviewed as content.

- Rationale: keeps the attack surface of arbitrary skill content near zero — worst case a bad skill mis-guides the chat, and its only "side-effecting" tool is writing a validated requisition file, which the model may only use after explicit user confirmation.

### Decision 7 — `create_requisition` tool writes a JSON file to a writable `requisitions/` directory

The framework registers a `create_requisition` tool with exactly six required parameters — `SupplierCode` (string), `Item` (string), `Description` (string), `Quantity` (number), `Date` (string, ISO `yyyy-MM-dd`), `Requester` (string). Its tool description instructs the model to call it **only after the user explicitly confirms a drafted requisition**, and to never invent field values. The tool resolves to `IRequisitionWriter.WriteAsync(NewPurchaseRequisition, ...)`:

- `NewPurchaseRequisition` is a plain DTO carrying the six fields (defined in `PrRag.Application/DTOs`).
- `FileRequisitionWriter` (Infrastructure) serializes the DTO to JSON and writes it atomically (temp file + rename) into the configured directory `Requisitions__Directory` (default `./requisitions`, resolving to `/app/requisitions` inside the API image), using a unique, sortable filename (e.g. `requisition-<timestamp>-<guid>.json`). Missing/invalid required fields return an explicit error string (surfaced as the tool result) and write no file.
- The validation is structural (required, non-empty, `Quantity` numeric, `Date` parseable) — business validation against the corpus happens upstream via `search_by_codes` during the guided flow.

DI: `RequisitionsSettings` bound like the other `IOptions<T>` sections (section `Requisitions`, property `Directory`) and configured through the `Requisitions__Directory` env var — the same pattern as `RAG__Report__OutputDirectory`, with `Requisitions__Directory=./requisitions` documented in `.env`/`.env.example` and the compose `api`/`devcontainer` services interpolating `${Requisitions__Directory:-...}` (container default `/app/requisitions`, devcontainer default `/workspaces/requisitions`); `IRequisitionWriter` is registered as a singleton. Compose adds a writable bind mount `./requisitions:/app/requisitions` to the `api` service (mirroring `./reports:/app/reports`).

- Rationale: matches the existing "content on disk" philosophy (`reports/` volume, `data/` mounts) and gives a human-reviewable artifact per created requisition without touching PostgreSQL or the API contract.
- Alternative considered A (persist requisitions into the DB like ingested ones): rejected — the user explicitly asked for JSON files under `requisitions/`; DB ingestion remains a separate pipeline.
- Alternative considered B (a dedicated REST endpoint `POST /api/requisitions`): rejected — the tool stays inside the agentic loop so the model reuses the field data it already collected and validated, and the chat endpoints keep their contract.

## Risks / Trade-offs

- **False skill activation** (model triggers a skill for a plain question) → seed descriptions with explicit "use ONLY when..." wording; manifest lists skills explicitly; when in doubt the model falls back to normal Q&A; observable via `SkillAtivated` in the report.
- **Skill body drifts / version mismatch** → `version` in front matter is surfaced in the manifest and logged on reload; no enforcement in v1.
- **History-truncated skill context** → model sees no `[Skill: <name>]` marker and behaves like a fresh chat; acceptable degradation, user can restate intent; mitigated by re-injection scanning available history.
- **Directory watcher in `:ro` production volume has no effect** → expected; production skill content is shipped at deploy time, watcher mainly serves dev; startup scan always runs so the deployed set is honored.
- **Model follows a low-quality skill and collects wrong/incomplete fields** → the `create-purchase-requisition` skill is curated; future skills are content-reviewed; report shows skill use for auditing.
- **Model writes a requisition the user never confirmed** (side-effecting tool) → strict tool description + skill instructions require explicit confirmation; structural validation only persists complete fields; accept a single unconfirmed-but-complete write as the residual risk until fenced tool permissions exist.
- **Duplicate or colliding requisition files** → unique timestamped/GUID filenames; atomic temp-file + rename means no partial files; a later change can add an idempotency key over the field values.

## Migration Plan

- Additive, non-breaking, no DB migration.
- Deploy: add `data/skills/` with the bundled `create-purchase-requisition.md`, set `Skills__Directory` (compose `api` service + devcontainer already mount `./data`), add the writable `./requisitions:/app/requisitions` mount and `Requisitions__Directory`, publish new image. Old image without the change ignores both directories entirely.
- Rollback: deploy previous image (skills dir and requisition writer are absent) or remove `data/skills/`.
- Tests: pure unit/integration inside the solution; fakes for the chat model; skills directory pointed at a temp fixture; requisition writer pointed at a temp folder.

## Open Questions

- Whether a future change should let skills declare extra tools or call HTTP endpoints — out of scope now; the manifest/body format leaves a compatible seam for it.
- Whether the frontend needs a subtle "guided by skill X" indicator or a dedicated skill status endpoint — deferred; normal streaming messages already surface the guided steps.