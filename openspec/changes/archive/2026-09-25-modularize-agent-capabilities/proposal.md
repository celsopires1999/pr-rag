## Why

Every part of the agent's model-facing contract lives in one place, so no part of it can change without touching all of it. `PurchaseRequisitionTools` is a 469-line class holding all seven tools, all seven wire-name constants, and the registration boilerplate; the prompt that documents those tools is a single 42-line `const` string in a different file; and `AgentInstructions` reaches into the tool set only through prose that a test asserts verbatim. Adding or moving a tool therefore means finding three files and hoping they stayed in sync — and nothing structurally ties the prompt's `<ALLOWED_ACTIONS>` bullets to the tools actually bound to the agent.

The read path and the write path are welded into the same unit. `create_requisition` is the only code in the repository that persists anything, and its confirmation gate (`CreateRequisitionAsync`, the highest-risk logic in the project) sits in the same class as read-only semantic search, wired to the same agent. That is a least-privilege problem as much as a cohesion problem, and it will block the orchestrator/specialist split this project is heading toward.

This is the first of two phases. Phase 1 (this change) is a behavior-neutral refactor that introduces the seam. Phase 2 replaces the single `ChatClientAgent` with an orchestrator plus specialists, built out of that seam.

## What Changes

- **Split the monolithic tools class into three per-capability units** — a retrieval specialist (`search_by_codes`, `search_semantic`, `get_suppliers_by_item`), a creation specialist (`create_requisition_draft`, `confirm_requisition_draft`, `create_requisition`), and a skill-activation specialist (`activate_skill`) — each owning its own handlers, its own tool registrations, and the prompt fragment that documents them. Handlers move verbatim: wire names, `[Description]` text, return types, and every message string in `ToolResults` are preserved character-for-character.
- **Introduce a capability catalog** as the single seam that binds tools to prompt text. Each capability contributes a `SpecialistDefinition(Id, DisplayName, ActionBlock, Tools)` pairing, so a capability's tools and its documentation cannot drift apart. In Phase 1 the catalog binds every capability into one agent; in Phase 2 each definition becomes one specialist agent.
- **Move the seven wire-name constants** out of the tools class into a single `ToolNames` type, so the "declared once" invariant survives the class split and remains usable by tests, prompt fragments, and the future routing layer.
- **Extract the registration/logging/bookkeeping boilerplate** into a small composed `SpecialistToolSet` helper, replacing three would-be copies. It is composed rather than inherited, matching the project's existing composition-over-inheritance style and keeping the `[Description]`-on-handler rule intact.
- **Trim `CoreInstructions` to cross-cutting text only** — identity, the ReAct loop, the data dictionary, and the universal guardrails. The per-tool `<ALLOWED_ACTIONS>` bullets and the creation-gate rules move into the owning capability's action block. `AgentInstructions` remains the prompt composer, so the `maf-agent-integration` requirement that compiled instructions come from a dedicated type still holds.
- **Fix the empty-manifest contradiction.** The "always check if there is a matching skill" rule currently sits in `CoreInstructions` and is therefore emitted even when the manifest is empty — telling the model to consult a catalog that the same prompt declares empty. It moves into the skill block, which is already omitted in that case.
- **Move the system prompt from hand-assembled first-turn message to `ChatClientAgent.Instructions`.** Today `ChatService` emits the prompt as a `ChatRole.System` message only on a session's first turn, so the skill manifest is composed once per session and a `SkillsWatcherService` reload never reaches a live session — contradicting the existing `skill-framework` requirement that newly loaded skills appear in subsequent requests. With the prompt on `Instructions`, it is recomposed per request. `instructions` is an optional parameter on the `AsAIAgent` overload already in use, so this is a one-argument change.
- **Rename the agent to `purchase-requisition-orchestrator`** and pin the two future specialist names (`purchase-requisition-retrieval-specialist`, `purchase-requisition-creation-specialist`) as capability ids. `AgentDescription` is unchanged — it reads correctly in both phases.
- No new tools, no tools removed, no wire name changed, no guardrail moved out of code, no change to the observability report schema, the chat response body, the API surface, the database, or the front end.

## Capabilities

### New Capabilities

- `specialist-capabilities`: A capability is a self-contained unit that owns both the tools it exposes and the prompt text documenting them, so the two cannot drift. The catalog is the single binding point between capabilities and the agent, and it is the seam the Phase 2 orchestrator/specialist split is built on.

### Modified Capabilities

- `agent-framework-layering`: the tools-class requirement still describes a "fixed four-tool list" and ties discovery to one tools class. It becomes catalog-based — the agent is composed from a catalog of capability definitions rather than a single tools class, and wire names are declared once in a shared type that survives the class split. The prompt's per-tool action text becomes part of the owning capability rather than a single monolithic block.
- `maf-agent-integration`: tool registration and agent composition are described in terms of one tools class and a prompt hand-assembled by the chat service. Both change: the agent is composed from the catalog with the prompt supplied through the agent's `Instructions`, and a new requirement states that the compiled prompt is recomposed per request so a live skill-manifest reload reaches in-flight sessions.

## Impact

- `PrRag.Application`:
  - New `Services/Agents/ToolNames.cs` — the seven wire-name constants.
  - New `Services/Agents/Specialists/` — `ISpecialistCatalog`, `SpecialistCatalog`, `SpecialistDefinition`, `SpecialistToolSet`, `RequisitionSearchSpecialist`, `RequisitionCreationSpecialist`, `SkillActivationSpecialist`.
  - `AgentInstructions` — `CoreInstructions` trimmed to cross-cutting text; `ComposeSystemPrompt` takes the catalog's action blocks; `AgentName` renamed.
  - `AgentRunService` — composed from the catalog; gains `ISkillService`; supplies `instructions:`.
  - `ChatService` — no longer emits the first-turn system message; still injects the active-skill body.
  - `DependencyInjection` — one scoped tools registration becomes four.
  - `PurchaseRequisitionTools` — **deleted**; its handlers and constants move to the specialists and `ToolNames`.
- `PrRag.Tests` — three files take mechanical edits, roughly 15 lines total: `ToolSchemaTests` (nine const references plus the `Functions()` helper resolving the catalog), `AgentFrameworkLayeringTests` (prompt-composition signature, DI resolution, const references, plus a new assertion that the three specialists partition the seven tools), `GetSuppliersByItemTests` (two const references). **No assertion changes intent** — `SkillFrameworkTests`, `RequisitionFlow`, `AgenticRetrievalTests`, `RagObservabilityReportTests`, `ItemSupplierCombinationTests`, and every `ToolSchemaTests` assertion on descriptions, schemas, and result shapes are untouched. That is the proof the refactor is behavior-neutral.
- One new regression test guards the prompt migration: turn 2 of a session must contain exactly one skill-manifest section, proving the framework does not accumulate a copy of the injected instruction per turn.
- `AGENTS.md` — the "Adding an agent tool" checklist gains a step (add the action-block bullet to the owning specialist) and a note that Phase 2 exists and what it must not regress.
- Phase 2 is explicitly **not** in this change: orchestrator/specialist runtime, `Microsoft.Agents.AI.Workflows` adoption, handoff, the specialist-session model, and the skill-markdown rewrite. The `RagQueryReport` schema stays frozen; delegation visibility in Phase 2 is via `ILogger` only.
