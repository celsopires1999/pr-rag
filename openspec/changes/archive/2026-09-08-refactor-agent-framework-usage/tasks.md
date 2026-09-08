## 1. Composition layer (Application)

- [x] 1.1 Create `AgentInstructions` type in `PrRag.Application` (e.g. `Services/Agents/AgentInstructions.cs`) holding agent name, description, and composable prompt fragments moved verbatim from `ChatService` (core system prompt, tools guide, guardrails).
- [x] 1.2 Add a `Compose(ISkillService)` (or equivalent) method that builds the final compiled system prompt including the dynamic skills manifest section; move the `BuildSystemPrompt` logic from `ChatService` here.
- [x] 1.3 Add an `AgentSpec` (or use `AgentInstructions` directly) exposing `Name`, `Description`, and `InstructionsFactory` so agent composition has a single identity source.

## 2. Tools class (Application)

- [x] 2.1 Create `PurchaseRequisitionTools` in `PrRag.Application` (e.g. `Services/Agents/PurchaseRequisitionTools.cs`).
- [x] 2.2 Move the four tool handlers (`search_by_codes`, `search_semantic`, `activate_skill`, `create_requisition`) into the class as `[Description]`-annotated methods, with the string descriptions and logic copied verbatim from `ChatService`.
- [x] 2.3 Replace `ChatService`'s per-run instance fields (`_activeTopK`, `_activeMinSimilarity`, `_activeRetrievedItems`, `_activeRewrittenQuery`) with per-turn state carried via a turn context or the session state bag; the tools class reads/writes this state.
- [x] 2.4 Expose `All` (a `IReadOnlyList<AITool>`) built via `AIFunctionFactory.Create`, mirroring the AgentLab `FeatureTools` pattern.

## 3. Run abstraction (Application)

- [x] 3.1 Define `IAgentRunService` (`RunAsync`, `RunStreamingAsync`) in `PrRag.Application/Abstractions`.
- [x] 3.2 Implement the run service in `PrRag.Application` composing `IChatClient.AsAIAgent(...)` from `AgentInstructions`/`AgentSpec` + `PurchaseRequisitionTools.All`, and delegating to `RunAsync`/`RunStreamingAsync`.

## 4. Skill state helper (Application)

- [x] 4.1 Create `SkillSessionState` helper encapsulating the state-bag keys (`SkillId`, `SkillBody`, `SkillBodyInjected`) and the read/inject/clear/report helpers moved from `ChatService`.
- [x] 4.2 Wire the tools class and report path to use the helper instead of inline key constants.

## 5. DI rewiring

- [x] 5.1 Register `AgentInstructions`/`AgentSpec`, `PurchaseRequisitionTools`, and `IAgentRunService` in `PrRag.Application/DependencyInjection.cs`.
- [x] 5.2 Ensure `PrRag.Infrastructure/DependencyInjection.cs` still registers `IChatClient` and passes the fixed tool list into the run-service composition; confirm the agent is composed in DI, not in `ChatService`.
- [x] 5.3 Remove any now-unused helper/registration duplication introduced by the split.

## 6. Refactor ChatService (Application)

- [x] 6.1 Remove from `ChatService`: inline `RegisterFunction`/tool handlers, `_agent` construction (`AsAIAgent`), prompt constant/`BuildSystemPrompt`, and skill-state key helpers.
- [x] 6.2 Update `ChatService` to depend on `IAgentRunService`, `AgentInstructions` (prompt for first-turn system message), and `SkillSessionState`; keep DTO mapping, session resolution, per-turn message assembly, and report writing.
- [x] 6.3 Confirm `AnswerAsync`/`StreamAsync` behavior, `session_id` echo, and SSE output are unchanged.

## 7. Tests

- [x] 7.1 Update `IntegrationServiceFactory`/fakes to register `IAgentRunService` (reusing `FakeChatClient`) and the new Application types.
- [x] 7.2 Add/adjust tests: `AgentInstructions` prompt composition (skills manifest), `PurchaseRequisitionTools` registration surface, and skill-state helper read/inject/clear.
- [x] 7.3 Update existing chat, skill, retrieval, and observability tests that referenced the old `ChatService` internal wiring.

## 8. Verification

- [x] 8.1 Run `dotnet build backend/PrRag.sln` and fix compile errors.
- [x] 8.2 Run `TEST_CONNECTION_STRING=... dotnet test backend/tests/PrRag.Tests` (or the compose test profile) and ensure the suite passes.
- [x] 8.3 Run `cd frontend && npm install && npm run build` to confirm the external contract is unchanged.