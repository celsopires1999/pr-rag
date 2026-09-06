## Context

The project is a .NET 10 layered solution (Api → Infrastructure → Application) implementing a RAG-powered chat assistant over purchase requisitions. The current `ChatService` (Application layer) contains a hand-rolled ReAct reasoning loop: it registers four tool functions via `AIFunctionFactory`, calls `IChatClient.GetResponseAsync` in a while-loop, inspects `FunctionCallContent` in the assistant response, invokes tools, appends `FunctionResultContent` messages, and repeats until the model produces a final text answer. Multi-turn state is managed manually by rebuilding the message list from plain-text history on each turn, with skill state tracked via mutable fields and a `[Skill: name]` regex marker.

This monolithic approach works for a single-agent RAG scenario but does not scale to multi-agent orchestration. The Microsoft Agent Framework (MAF) 1.20.0 — the production successor to AutoGen + Semantic Kernel — provides `AIAgent` (tool-using agent with built-in tool-call loop), `AgentSession` (conversation state management), middleware pipeline, and workflow orchestration (sequential, concurrent, handoff). MAF builds on top of `IChatClient` from Microsoft.Extensions.AI, which is already the project's chat abstraction.

## Goals / Non-Goals

**Goals:**
- Replace the manual tool-call loop in `ChatService` with MAF's `AIAgent.RunAsync()` for the agentic reasoning loop.
- Use `AgentSession` for multi-turn conversation state managed **server-side**, keyed by a client-supplied session identifier, so the client does not have to resend the full history each turn.
- Register the four existing tools (`search_by_codes`, `search_semantic`, `activate_skill`, `create_requisition`) as MAF tools.
- Move skill state into the `AgentSession` state bag, removing the fragile `[Skill: name]` marker system.
- Preserve external contracts: endpoints, DTOs, SSE streaming format, and the frontend chat behavior (with a minimal frontend change to send a session id).
- Create a foundation for future multi-agent workflows (handoff, sequential) without implementing them now.
- All existing tests must pass with minimal changes to test infrastructure.

**Non-Goals:**
- Implementing multi-agent workflows (handoff, sequential, concurrent) — this is a future change.
- Changing the RAG retrieval logic or tool semantics.
- Changing the skill framework or its markdown parsing.
- Modifying the ingestion pipeline, data models, or EF Core schemas.
- Adding new API endpoints or changing existing ones (the existing endpoints are reused; only an optional `session_id` field is added to the request/response DTOs).
- Migrating to a different LLM provider (staying on OpenAI).

## Decisions

### Decision 1: Use `AIAgent` with `AsAIAgent()` extension on `IChatClient`

**Choice**: Create the `AIAgent` from the existing `IChatClient` using the `AsAIAgent()` extension method, then call `agent.RunAsync()` for each chat request.

**Rationale**: `AsAIAgent()` wraps the existing `IChatClient` (which is already registered via `services.AddChatClient(...)`) into an `AIAgent` with minimal friction. The agent inherits the same OpenAI model, API key, and configuration. This avoids introducing a second chat client registration.

**Alternatives considered**:
- *New `AIAgent` directly from `ChatClient`*: Would bypass the existing `IChatClient` registration and duplicate configuration. Rejected to avoid config duplication.
- *Use `AgentWorkflowBuilder` for single agent*: Over-engineered for a single-agent scenario. Workflows are for multi-agent orchestration, which is out of scope.

### Decision 2: `AgentSession` for multi-turn state, keyed by a client-sent session id

**Choice**: Use `AgentSession` to manage conversation history server-side. An in-memory `IAgentSessionStore` keeps one `AgentSession` per session id. The client generates a session id (persisted in `localStorage`) and sends it on every request; the server resolves/creates the matching `AgentSession`. Since MAF's `AgentSession` accumulates the full message history across `RunAsync` calls on the same instance, the client no longer needs to resend the history each turn, and the manual `BuildConversationAsync` history reconstruction and the `[Skill: name]` marker regex hack are removed.

**Rationale**: MAF's `AgentSession` is the first-class mechanism for multi-turn state. Persisting it server-side keyed by a client-supplied id keeps the API request small (question + session id + retrieval params), moves conversation state to the server as the user requested, and eliminates the fragile regex-based skill marker and mutable-field state management in `ChatService`. This is in-memory (per-process), so sessions are lost on restart — acceptable for the current single-instance deployment, with persistence to Postgres called out as a future option.

**Alternatives considered**:
- *Client sends full history each turn (stateless)*: Fails the goal — the client would still own/resent conversation state. Rejected.
- *Server-generated session id returned to client*: Works, but requires an extra round-trip to learn the id and a response contract change on the first turn. The client-sent id is simpler and matches how the frontend already holds its own message list.
- *Persist `AgentSession` state to Postgres*: More durable, but requires a new EF table/migration and serialization plumbing. Out of scope for this change; kept as a future option.

### Decision 3: Tools registered via `AIFunctionFactory` — same delegates, MAF surface

**Choice**: Keep the existing `AIFunctionFactory.Create(handler)` calls but register them with the `AIAgent` via its `Tools` property instead of passing them through `ChatOptions`.

**Rationale**: The tool delegate signatures and implementations remain identical. Only the registration surface changes — from `ChatOptions.Tools` to `AIAgent.Tools`. The underlying `IEmbeddingService`, `IPurchaseRequisitionRepository`, `ISkillService`, and `IRequisitionWriter` dependencies remain the same.

**Alternatives considered**:
- *MCP tool registration*: MAF supports MCP, but adding an MCP server for in-process tools is unnecessary overhead. Rejected.
- *Custom `AITool` implementations*: Would require reimplementing the `AIFunctionFactory` logic manually. Rejected — `AIFunctionFactory` is already battle-tested in this codebase.

### Decision 4: RAG observability stays post-hoc in `ChatService`

**Choice**: Keep assembling and writing `RagQueryReport` records post-hoc in `ChatService` via `WriteReportAsync`, rather than moving it into an `IAgentMiddleware`.

**Rationale**: MAF middleware is a valid observability hook, but all the report data (rewritten query, retrieved items, skill state, answer) is already assembled in `ChatService` instance/session state. Moving it into middleware adds indirection for no behavioral gain in this single-agent, stateless-by-request setup. The post-hoc approach keeps the change minimal and preserves the existing `WriteReportAsync` exception handling verbatim.

**Alternatives considered**:
- *MAF `IAgentMiddleware` for observability*: Clean in principle, but couples report assembly to the middleware pipeline and duplicates access to session state. Deferred; could be introduced later without changing the report format.
- *OpenTelemetry integration*: MAF has built-in OTel support, but the current project writes reports to disk files, not traces. OTel is out of scope for this migration.

### Decision 5: Skill state via `AgentSession.StateBag`, not regex markers

**Choice**: Store the active skill name and body in the `AgentSession` state bag when `activate_skill` is invoked, and re-inject the guidance as a system message on subsequent turns of the same session.

**Rationale**: `AgentSession.StateBag` supports `SetValue<T>(key, value)` / `TryGetValue<T>(key, out value)` and persists across agent runs within the same session. Because skill state now lives in the per-session state bag keyed by the client's session id, the fragile `[Skill: name]` marker prefix and `FindActiveSkillFromHistoryAsync`/`ApplySkillMarker`/`SkillMarkerRegex` all become unnecessary and are removed.

**Alternatives considered**:
- *Keep the marker system*: Works but is fragile (depends on LLM output format) and architecturally incorrect — state management belongs in the session, not in message content. Rejected.
- *Keep skill state as mutable fields on `ChatService`*: `ChatService` is scoped per request, so fields do not survive across stateless requests; at minimum the state would have to be reintroduced from the client each turn. With server-side sessions, the state bag is the correct home. Rejected.
- *Use `AIContextProvider`*: MAF's context provider is for cross-session memory (RAG-like context injection). Skill state is per-session. Rejected.

### Decision 6: Streaming via MAF's streaming API

**Choice**: Use `AIAgent.RunStreamingAsync()` for the SSE streaming path, adapting the existing `StreamAsync` method.

**Rationale**: MAF provides `RunStreamingAsync()` which yields `AgentTurnResponse` items. The streaming path yields tokens as they arrive, preserving the existing SSE contract.

**Alternatives considered**:
- *Keep the non-streaming path and add streaming separately*: Would duplicate the tool-call loop. Rejected since MAF handles both.
- *Use `IChatClient` streaming directly*: Would bypass MAF's middleware and session management. Rejected.

## Risks / Trade-offs

- **MAF version lock-in**: Upgrading from `IChatClient` loop to MAF ties us to `Microsoft.Agents.AI` APIs. Mitigation: MAF is built on `IChatClient` — if we ever need to revert, the underlying chat client abstraction is unchanged. The tool delegates and repository layer are completely independent of MAF.

- **Test infrastructure changes**: `FakeChatClient` currently implements `IChatClient` directly. MAF's `AIAgent` may need a different fake (e.g., a fake `IChatClient` that is wrapped with `AsAIAgent()`). Mitigation: `AsAIAgent()` works with any `IChatClient`, so the existing `FakeChatClient` should still work when wrapped. Verify during implementation.

- **Streaming behavior differences**: MAF's streaming API may yield tokens differently than the current manual `GetResponseAsync` → text extraction. Mitigation: The SSE contract is just `data: <line>\n` — as long as the final text is yielded incrementally, the frontend contract is preserved. Test with the existing frontend.

- **Session state overhead**: `AgentSession` adds state management overhead (serialization, deserialization) that the current mutable-field approach avoids. Mitigation: For a per-request session (stateless API), the overhead is minimal — session is created, used, and discarded per request.

- **Middleware execution order**: If multiple middlewares are added later, execution order matters. Mitigation: Document the middleware pipeline order. For now, only one middleware (observability) is needed.

## Migration Plan

1. Add NuGet packages (`Microsoft.Agents.AI`) to Infrastructure and Application.
2. Wrap `IChatClient` with `AsAIAgent()` in `ChatService`; register an in-memory `IAgentSessionStore` in DI.
3. Add an optional `session_id` to the request DTOs and echo it in `ChatResponse`.
4. Refactor `ChatService` to use `AIAgent.RunAsync()` / `RunStreamingAsync()` with a session resolved from the store.

**Rollback**: Revert to previous `ChatService.cs` and `DependencyInjection.cs`. No schema changes, no data migrations, no API contract changes — rollback is a code-only revert.

## Open Questions

- ~~Does `AsAIAgent()` on an existing `IChatClient` correctly inherit the same `ChatClient` configuration?~~ **Resolved**: yes — it wraps the registered `IChatClient` with the same model/API key.
- ~~Can `AgentSession` be created per-request (stateless API pattern) without issues?~~ **Resolved**: `AgentSession` accumulates full history across `RunAsync` calls on the same instance; we persist one per session id in an in-memory store rather than per request.
- ~~Does MAF's `RunStreamingAsync()` yield intermediate tool-call results, or only the final text?~~ **Resolved**: it yields final text deltas; tool calls are handled internally (verified with `FakeChatClient`).
