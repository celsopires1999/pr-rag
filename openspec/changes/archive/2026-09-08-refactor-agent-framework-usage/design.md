## Context

`ChatService` (in `PrRag.Application/Services/ChatService.cs`) currently owns the entire MAF integration as a ~436-line monolith: it constructs the `ChatClientAgent` in its constructor, registers four tools inline via `RegisterFunction`/`AIFunctionFactory`, resolves the `AgentSession`, manages skill state through magic-key constants in the session `StateBag`, injects skill body/system messages per turn, compiles the system prompt (including the dynamic skills manifest), and writes the RAG observability report post-hoc. Agent composition, orchestration, tool wiring, prompt compilation, and state handling are all mixed together.

The reference project (`AgentLab`, a Microsoft Agent Framework training sample) demonstrates a clean separation: an `AgentInstructions` type for naming + composable prompt fragments, a dedicated tools class with `[Description]`-annotated static methods wrapped via `AIFunctionFactory`, memory/state as a dedicated type, and slim orchestration samples that consume a composed agent. The project also has supporting MAF features (`AIContextProvider`, hosting extensions) we are not yet using.

This change re-layers the existing MAF integration along those lines without altering the external API or RAG behavior. It is a prerequisite for future multi-agent work.

## Goals / Non-Goals

**Goals:**
- Decompose `ChatService` so agent composition, tool registration, prompt compilation, and per-turn orchestration are distinct, reusable components.
- Introduce domain-layer abstractions so the composed agent can be resolved from DI and independently tested/faked.
- Encapsulate skill-state management and RAG tool logic outside the chat orchestrator.
- Keep the external contract (`POST /api/chat`, `POST /api/chat/stream`, SSE format, `session_id` echo) and the RAG observability report identical.
- Mirror the `AgentLab` structure: composition vs. orchestration vs. state as separate concerns.

**Non-Goals:**
- No new agent capabilities (no multi-agent workflows, handoffs, MCP, or DevUI).
- No change to retrieval semantics, skill semantics, or prompt content.
- No change to the frontend or the external API contract.
- No migration to new MAF packages (keep `Microsoft.Agents.AI` 1.20.0 already in use).

## Decisions

### Decision 1: Introduce an `AgentSpec` + `AgentInstructions` composition layer (Application)

Modeled on `AgentLab.AgentInstructions`, create an `AgentInstructions` type holding the agent name, description, and composable prompt fragments (core prompt + tools guide + skills section). A method compiles the final system prompt including the dynamic skills manifest from `ISkillService`. The agent identity (`name`, `description`) is defined here, not as magic strings in `ChatService`.

- **Why**: Prompt compilation and identity become a single, testable home; `ChatService` no longer owns a 100-line inline prompt constant. Mirrors AgentLab's composable-instructions pattern.
- **Alternatives considered**: Keep the prompt inline (no — leaves the monolith); move prompt to Infrastructure (no — prompt is domain content, Application layer is correct per dependency direction).

### Decision 2: Extract tools into a dedicated `PurchaseRequisitionTools` class (Application)

Move the four tool handlers into a class exposing `[Description]`-annotated methods, and provide an `AITool`/`List<AITool>` surface built via `AIFunctionFactory`. The tools receive their collaborators (repository, embedding service, skill service, requisition writer) and read per-turn parameters (topK, minSimilarity) from an injectable per-turn context/session rather than from `ChatService` fields.

- **Why**: Tool wiring leaves the orchestrator; tools become independently testable and reusable across future agents, matching AgentLab's `FeatureTools`.
- **Decision detail — per-turn state**: Currently `ChatService` stashes `_activeTopK`, `_activeMinSimilarity`, `_activeRetrievedItems`, `_activeSession` as instance fields mutated during a run. These are session-scoped. Introduce a small `AgentTurnContext` (or reuse the `AgentSession` state bag) to carry topK/minSimilarity and collect retrieved items, injected into the tools class per turn. **Alternative**: keep fields on `ChatService` and pass them into tools — rejected because it preserves the coupling we are removing. Use the `AgentSession` state bag so no new out-of-band registry is needed; session state already exists for skill handling.

### Decision 3: Introduce an `IAgentRunService` abstraction (Application)

Define `IAgentRunService` exposing `RunAsync(RunRequest)` and `RunStreamingAsync(RunRequest)` that take a question, session, and run options and return the answer. `ChatService` consumes this via DI instead of holding a `ChatClientAgent` it built itself. The concrete implementation composes the `IChatClient` → `AIAgent` and invokes `RunAsync`/`RunStreamingAsync`.

- **Why**: Breaks `ChatService`'s dependency on constructing the agent; gives a seam for tests (fake the run service) and for future multi-agent orchestration behind the same interface.
- **Alternatives considered**: Have `ChatService` keep the agent and only extract tools (no — orchestrator stays coupled to agent construction); register `AIAgent` directly in DI (possible but MAF agents are not always trivially DI-resolvable and expose more surface than needed — an interface narrows the contract).

### Decision 4: Compose the agent in Infrastructure DI

`AddInfrastructure` composes `IChatClient` → `AIAgent` (name/description/fixed tool list from the new types) and registers `IAgentRunService`; `ChatService` no longer builds the agent in its constructor. `AddApplication` registers `AgentInstructions`, the tools class, and `IAgentRunService` implementation if it stays in Application, or Infrastructure registers the run service that depends on Application interfaces.

- **Why**: The composition graph becomes explicit and testable; fakes can swap either the `IChatClient` (existing) or the whole run service.
- **Layer decision detail**: Keep the run-service implementation in **Application** because `IAgentRunService` is a domain abstraction and the current `ChatService` already does the agent run against Application-provided interfaces; Infrastructure only wires config/transport (`IChatClient`). This matches the existing split where `ChatService` (Application) already called `RunAsync`.

### Decision 5: Encapsulate skill-state read/inject/clear logic (Application)

Extract the state-bag key constants and the `ReadActiveSkillBody`/`ClearSkillState`/`GetActiveSkillForReport` helpers into a dedicated `SkillSessionState` helper type used by the tools class and the report path.

- **Why**: Removes magic-key constants and duplicated state logic from the orchestrator; gives a focused unit under test. Matches AgentLab's dedicated memory/state types.

### Decision 6: Keep `ChatService` as a thin orchestration adapter (Application)

`ChatService` retains: DTO mapping (`ChatRequest`/`ChatStreamRequest` → run request), `session_id` resolution, session get-or-create via `IAgentSessionStore`, per-turn message assembly (system prompt on first turn, skill-body reinjection), calling `IAgentRunService`, and writing the `RagQueryReport`. It delegates agent execution to the run service.

- **Why**: Preserves the public `IChatService` contract and all report/session behavior while removing construction/tool/prompt responsibilities. This is the minimal-risk decomposition.

## Risks / Trade-offs

- [Per-turn state moves from instance fields to session state bag] → Mitigation: encapsulate reads/writes in a single `SkillSessionState`/turn-context helper; add integration tests asserting topK/minSimilarity/retrieved-count still flow to the report.
- [New abstractions increase indirection] → Mitigation: keep interfaces small and aligned with existing `IChatService` shape; fake via existing `FakeChatClient` where possible.
- [Sealed record/state bag serialization constraints in MAF sessions] → Mitigation: store primitive values in the state bag only, as today; no complex types introduced.
- [Behavioral drift in prompt/tool descriptions during extraction] → Mitigation: reuse the exact same prompt and `[Description]` strings verbatim when moving code; verify via existing chat tests.

## Migration Plan

1. Add `AgentInstructions`/`AgentSpec` (move prompt + identity verbatim).
2. Add `PurchaseRequisitionTools` (move tool handlers + `[Description]` verbatim; switch per-turn params to the session state bag/turn context).
3. Add `IAgentRunService` + implementation (compose agent, `RunAsync`/`RunStreamingAsync`).
4. Add `SkillSessionState` helper (move state-bag helpers).
5. Rewire DI: Application registers new types and run service; Infrastructure registers `IChatClient` (unchanged) and passes fixed tool list to run-service composition.
6. Refactor `ChatService` to consume `IAgentRunService` + the new helpers; delete inlined agent/tool/prompt/state code (net reduction in size).
7. Update test fakes/factory; run `dotnet build` and the integration suite; run frontend build to confirm contract unchanged.
8. Rollback: revert commits; external contract unchanged so no client/deploy coordination needed.

## Open Questions

- Whether `IAgentRunService` should also own the fixed tool list (Decision 4) or receive tools from `AgentInstructions` — resolved in favor of the tools/composition living together with the agent, but confirm during implementation that the tool list is registered once, not per `ChatService` instance.
- Whether per-turn topK/minSimilarity/retrieved-items should live in the session `StateBag` (survives across turns) or a transient in-memory turn context (cleared each turn). The report only needs per-turn values; keep them transient and separate from persisted skill state.
