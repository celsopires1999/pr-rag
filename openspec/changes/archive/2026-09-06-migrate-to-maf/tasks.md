## 1. NuGet Packages and DI Registration

- [x] 1.1 Add `Microsoft.Agents.AI` (1.20.0) to `PrRag.Infrastructure.csproj` and `PrRag.Application.csproj` (bump `Microsoft.Extensions.*` to 10.0.11)
- [x] 1.2 Wrap the existing `IChatClient` with `AsAIAgent()` and use it in `ChatService`
- [x] 1.3 Add an `IAgentSessionStore` (in-memory `AgentSession` store keyed by session id) and register it in DI

## 1.5. Session Identifier Contract

- [x] 1.5.1 Add an optional `session_id` to `ChatRequest` and `ChatStreamRequest` DTOs
- [x] 1.5.2 Echo the resolved `session_id` in `ChatResponse` so a first-time client can adopt it
- [x] 1.5.3 Frontend: generate and persist a `session_id` (e.g. in `localStorage`) and send it on every request

## 2. Agent Tool Registration

- [x] 2.1 `ChatService` constructs and owns the `ChatClientAgent` (wrapping `IChatClient`) and holds per-run tools
- [x] 2.2 Register the four tools (`search_by_codes`, `search_semantic`, `activate_skill`, `create_requisition`) via `AIFunctionFactory`, passed to the run through `ChatOptions.Tools`
- [x] 2.3 Verify that tool delegate signatures and `AIFunctionFactory.Create` calls remain unchanged

## 3. Agent Execution — Non-Streaming Path

- [x] 3.1 Replace the manual `ResolveContextAsync` while-loop in `AnswerAsync` with `AIAgent.RunAsync()`
- [x] 3.2 Initialize/resolve the `AgentSession` from the server-side store keyed by the request's `session_id` instead of reconstructing history from the request
- [x] 3.3 Extract the final answer text and retrieved items from the agent response
- [x] 3.4 Ensure `ChatResponse` DTO is populated identically (Answer + RetrievedCount + SessionId)

## 4. Agent Execution — Streaming Path

- [x] 4.1 Replace the `StreamAsync` implementation to use `AIAgent.RunStreamingAsync()`
- [x] 4.2 Yield answer tokens incrementally from the streaming agent response
- [x] 4.3 Ensure the SSE `data: <line>\n` format is preserved for the frontend

## 5. Skill State via Session

- [x] 5.1 Store active skill name and body in `AgentSession.StateBag` when `activate_skill` is invoked (tool writes to the active session's state)
- [x] 5.2 Restore and re-inject skill guidance from `AgentSession.StateBag` on subsequent turns in the same session
- [x] 5.3 Remove `ApplySkillMarker`, `SkillMarkerRegex`, `FindActiveSkillFromHistoryAsync`, and `ResetSkillState` from `ChatService`
- [x] 5.4 Remove the mutable `_activeSkillId`/`_activeSkillName`/`_skillActivated`/`_requisitionCreated` fields (replaced by session state)

## 6. System Prompt and Configuration

- [x] 6.1 Keep `SystemPrompt` in `ChatService`; pass the dynamic system prompt (including the skills manifest) as the first system message when a session is first created; rely on `AgentSession` accumulation afterwards (do not re-inject to avoid duplication)
- [x] 6.2 Verify that the skills manifest is injected into the system prompt on session creation
- [x] 6.3 Verify that `RagSettings` (TopK, MinSimilarity) are still accessible to the tool delegates

## 7. Observability

- [x] 7.1 Keep report assembly post-hoc in `ChatService` (`WriteReportAsync`) — no MAF middleware (design deviation: report writing stays in the service)

## 8. Test Infrastructure

- [x] 8.1 Verify that `FakeChatClient` works when wrapped with `AsAIAgent()` for both non-streaming and streaming paths (streaming adapted to mirror the scripted replies)
- [x] 8.2 Register the in-memory `IAgentSessionStore` and `FakeChatClient` in `IntegrationServiceFactory`
- [x] 8.3 Update `AgenticRetrievalTests` to pass `session_id` and verify history accumulates server-side
- [x] 8.4 Update `SkillFrameworkTests` to verify skill activation/restoration via `AgentSession.StateBag` (no `[Skill: name]` marker)
- [x] 8.5 Run `IngestionDiffTests`, `RagObservabilityReportTests`, `SkillsDirectoryTests`, and `FileRequisitionWriterTests` — these should be unaffected

## 9. Build and End-to-End Verification

- [x] 9.1 Run `dotnet build backend/PrRag.sln` and resolve any compilation errors
- [x] 9.2 Run `dotnet test backend/tests/PrRag.Tests` with a reachable Postgres instance and confirm all tests pass
- [x] 9.3 Start the API + frontend via `docker compose --profile demo up -d` and manually verify chat (streaming + non-streaming), skill activation, and requisition creation in the browser
- [x] 9.4 Verify the RAG observability report is written to `./reports` after each chat answer
