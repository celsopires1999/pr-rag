## 1. Models, configuration, and abstractions

- [x] 1.1 Add `Skill` model (record) in `PrRag.Application/Domain` with `Id`, `Name`, `Description`, `Version`, `Body` (markdown guidance), plus a `SkillManifestEntry` (name + description) for the system prompt
- [x] 1.2 Add `SkillsSettings` class in `PrRag.Application/Configuration` (section name `Skills`, property `Directory` defaulting to `/data/skills`)
- [x] 1.3 Add `ISkillService` abstraction in `PrRag.Application/Abstractions` with `GetManifest()`, `GetSkillAsync(string name)`, and `ReloadAsync(CancellationToken)`
- [x] 1.4 Add `NewPurchaseRequisition` DTO in `PrRag.Application/DTOs` with exactly `SupplierCode` (string), `Item` (string), `Description` (string), `Quantity` (number), `Date` (string ISO `yyyy-MM-dd`), `Requester` (string)
- [x] 1.5 Add `RequisitionsSettings` class in `PrRag.Application/Configuration` (section name `Requisitions`, property `Directory` defaulting to `./requisitions`, mirroring the `RAG__Report__OutputDirectory` report pattern)
- [x] 1.6 Add `IRequisitionWriter` abstraction in `PrRag.Application/Abstractions` with `WriteAsync(NewPurchaseRequisition requisition, CancellationToken)` returning the created file name

## 2. Skill store and requisition writer (Infrastructure)

- [x] 2.1 Implement `SkillsDirectory` in `PrRag.Infrastructure/Services`: scans the configured directory for `*.md` files, parses YAML front matter (`name`, `description`, `version`) plus the guidance body, and builds the in-memory manifest/body map; skips files missing required front matter (logged, not fatal)
- [x] 2.2 Add a debounced `FileSystemWatcher` reload to `SkillsDirectory` (mirroring `FileWatcherService`) so new/edited skill files are re-discovered during development
- [x] 2.3 Implement `FileRequisitionWriter` in `PrRag.Infrastructure/Services`: serializes the DTO to JSON and writes atomically (temp file + rename) into the configured requisitions directory with a unique timestamped/GUID filename; validates required fields and returns an error string (and writes no file) on missing/invalid values
- [x] 2.4 Register `SkillsSettings`, `RequisitionsSettings` via `services.Configure`; register `ISkillService` (singleton) and `IRequisitionWriter` (singleton) in `PrRag.Infrastructure/DependencyInjection.cs`
- [x] 2.5 Configure `Skills__Directory` for the `api` and `devcontainer` services in `docker-compose.yml` (points at the bind-mounted `data/skills`), keeping the runtime volume read-only
- [x] 2.6 Add the writable bind mount `./requisitions:/app/requisitions` to the `api` service (mirroring `./reports:/app/reports`) and set `Requisitions__Directory` via the `Requisitions__Directory` env var (interpolated with a `/app/requisitions` container default; `./requisitions` in `.env`), for both `api` and `devcontainer` — same env-var pattern as `RAG__Report__OutputDirectory`

## 3. ChatService integration

- [x] 3.1 Build the system prompt Skills section from `ISkillService.GetManifest()` in `ChatService.BuildConversation`, listing `- <name>: <description>` for every loaded skill (empty prompt section when no skills exist)
- [x] 3.2 Register an `activate_skill(string name, CancellationToken ct)` tool in `ChatService` that returns the loaded skill's guidance body for known skills and an error listing available skills for unknown ones
- [x] 3.3 Register a `create_requisition(string SupplierCode, string Item, string Description, decimal Quantity, string Date, string Requester, CancellationToken ct)` tool in `ChatService` that delegates to `IRequisitionWriter`; tool description instructs calling it only after explicit user confirmation and never inventing values, and surfaces the writer's error result on invalid input
- [x] 3.4 Support cross-turn persistence in `BuildConversation`: scan the incoming `Messages` history for the most recent assistant message with a leading `[Skill: <skill-name>]` marker and, when found, inject that skill's guidance as an additional system message before the latest user turn (history DTOs are plain text, so the marker line substitutes for scanning a prior function call)
- [x] 3.5 Add the rules block for active skill turns in the system prompt: skill guidance applies on top of existing guardrails, and skills never change the available tool set

## 4. Observability

- [x] 4.1 Add `SkillId`, `SkillName`, and `SkillActivated` fields to `RagQueryReport` (`PrRag.Application/DTOs/RagReportDtos.cs`)
- [x] 4.2 In `ChatService.AnswerAsync`/`StreamAsync`, set the report skill fields when the resolved request had an active skill (via live tool call or restored from history)
- [x] 4.3 Verify `FileRagReportWriter` serializes the new fields without changing the report contract for skill-free requests

## 5. Bundled purchase-requisition skill

- [x] 5.1 Create `data/skills/create-purchase-requisition.md` with YAML front matter (`name`, `description`, `version`) and a guidance body covering: acting as a purchasing assistant; collecting item code(s), quantity, unit of measure, supplier, expected delivery date, requester, and justification one question at a time; validating `ITM-*`/`SUP*` codes via the existing search tool and flagging unmatched codes; producing a structured draft for confirmation; and calling `create_requisition` with the six fields (SupplierCode, Item, Description, Quantity, Date, Requester) after the user confirms
- [x] 5.2 Ensure the skill is discoverable by the default `Skills__Directory` path mounted in compose

## 6. Tests

- [x] 6.1 Unit test `SkillsDirectory` loading: valid files parsed, missing/invalid front matter skipped, absent/empty directory yields empty manifest
- [x] 6.2 Unit test `FileRequisitionWriter`: valid requisition writes a JSON file (in the configured directory) whose content is exactly the six fields; missing/invalid fields write no file and return an error
- [x] 6.3 Extend `FakeChatClient` (or add a fake) so existing agentic tests can exercise the `activate_skill` and `create_requisition` tools end to end
- [x] 6.4 Integration test: model calls `activate_skill` for the `create-purchase-requisition` skill and the assistant follows the skill guidance, including validating an item code via `search_by_codes`
- [x] 6.5 Integration test: unknown skill name returns the available-skills error and the conversation continues normally
- [x] 6.6 Integration test: a follow-up stream request (with history containing an assistant message starting with `[Skill: create-purchase-requisition]`) has the skill guidance restored and applied
- [x] 6.7 Integration test: after user confirmation the model calls `create_requisition` with the collected fields and the JSON file is written to the requisitions directory with the expected content
- [x] 6.8 Integration test: `create_requisition` with missing/invalid fields returns the error and writes no file
- [x] 6.9 Integration test: a plain question with no skill loaded (or no match) answers in free-form with `SkillActivated == false`
- [x] 6.10 Integration test: the observability report for a skill-guided request records `SkillId`, `SkillName`, and `SkillActivated = true`

## 7. Verification

- [x] 7.1 Run `dotnet build backend/PrRag.sln` and `dotnet test backend/tests/PrRag.Tests` (with a reachable Postgres via `TEST_CONNECTION_STRING` or the compose test profile) — passed inside the devcontainer, no OpenAI key needed (fakes used), 27/27
- [x] 7.2 Run a manual demo with the compose `demo` profile: ask to create a purchase requisition and confirm the guided flow, code validation, draft summary, user confirmation, and the JSON file written under `requisitions/` — done with real OpenAI; the flow activated the skill, emitted/restored the `[Skill: ...]` marker across turns, validated `SUP000009`/`ITM-00000000000000000001` via `search_by_codes`, and the confirmed requisition was persisted to `requisitions/requisition-*.json`. Two issues surfaced and fixed during the demo:
  - the model did not emit the marker reliably → `ChatService.ApplySkillMarker` now guarantees it (and drops it once a requisition is created);
  - `./requisitions` bind-mount was root-owned on a fresh start, so the non-root API container got a 500 on write → the runtime now uses an entrypoint (as root) that chowns `/app/reports` and `/app/requisitions` to `app` before dropping privileges.
- [x] 7.3 Confirm a chat about a plain factual question (no skill) behaves exactly as before — answered free-form ("Não tenho informações suficientes...") with `SkillActivated == false` and no skill fields in the report
- [x] 7.4 Check a skill-generated RAG report in `reports/` shows the skill fields — every turn of the demo recorded `SkillId: create-purchase-requisition`, `SkillName: create-purchase-requisition`, `SkillActivated: true` (and `RetrievedItems` for the code-validation turn)