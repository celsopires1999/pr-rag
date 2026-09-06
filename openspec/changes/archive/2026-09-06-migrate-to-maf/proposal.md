## Why

The project currently uses a hand-rolled ReAct agentic loop in `ChatService` built on raw `IChatClient` + `AIFunctionFactory`. This works but couples orchestration logic (tool-call loop, skill state, report generation) to a single monolithic service. As this project becomes the base for a multi-agent implementation, we need a proper agent orchestration framework. **Microsoft Agent Framework (MAF) 1.20.0** — the production successor to AutoGen + Semantic Kernel — provides `AIAgent`, `AgentSession`, workflow orchestration, and middleware, all built on top of the same `IChatClient` abstraction we already use. Migrating now gives us a standards-based agent layer, session/state management for free, and a clear path to multi-agent workflows (handoff, sequential, concurrent) without rewriting the retrieval and skill infrastructure.

## What Changes

- **BREAKING**: Replace the manual ReAct tool-call loop in `ChatService` with MAF's `AIAgent` + `AgentSession`, which handles tool invocation, multi-turn state, and conversation management.
- Add the `Microsoft.Agents.AI` NuGet package to the Infrastructure and Application layers.
- Make the chat agent **stateful**: an in-memory `IAgentSessionStore` keeps one `AgentSession` per client-supplied `session_id`. The frontend generates a `session_id` (persisted in `localStorage`) and sends it on every request, so the server manages conversation history and the client no longer resends the full history each turn.
- Add an optional `session_id` to the request DTOs and echo the resolved id in `ChatResponse`.
- Refactor `ChatService` into a thin `IChatService` adapter that delegates to `AIAgent.RunAsync()` / `AgentSession` for conversation execution.
- Expose the four existing tools (`search_by_codes`, `search_semantic`, `activate_skill`, `create_requisition`) as MAF tools via `AIFunctionFactory` — same delegates, new registration surface.
- Move skill state into the `AgentSession` state bag, removing the `[Skill: name]` marker prefix and its regex parsing.
- Keep RAG observability report generation post-hoc in `ChatService` (not a middleware).
- Preserve the existing external API surface (`POST /api/chat`, `POST /api/chat/stream`, `POST /api/ingest`, `GET /api/status`, SSE format), with a minimal frontend change to send `session_id`.
- Update test fakes (`FakeChatClient`) to work with the MAF agent abstraction (including streaming) and register the in-memory session store in the test container.

## Capabilities

### New Capabilities
- `maf-agent-integration`: Core integration of Microsoft Agent Framework — `AIAgent` creation via `AsAIAgent()`, tool binding, and a server-side `AgentSession` keyed by `session_id` for multi-turn and skill state.

### Modified Capabilities
- `agentic-retrieval`: Tool registration and invocation mechanism changes from raw `AIFunctionFactory` + manual loop to MAF `AIAgent` tool binding. Retrieval semantics and tool contracts remain unchanged.
- `continuous-conversation`: Multi-turn state moves to a server-side `AgentSession` keyed by `session_id` (replacing manual history reconstruction). Conversation semantics remain unchanged.
- `chat-query`: Chat service implementation changes from `IChatClient.GetResponseAsync` loop to `AIAgent.RunAsync`, adding an optional `session_id`. The `IChatService` contract and response format remain unchanged (session id is echoed).
- `chat-streaming`: Streaming path adapts to MAF's streaming API over a server-side session. SSE contract remains unchanged.

## Impact

- **NuGet packages**: Add `Microsoft.Agents.AI` (1.20.0) to `PrRag.Infrastructure.csproj` and `PrRag.Application.csproj`; bump the `Microsoft.Extensions.*` transitive packages to 10.0.11.
- **PrRag.Application**: `ChatService.cs` major refactor (new execution model, server-side session, session-state skill handling). New `IAgentSessionStore` and in-memory implementation. `IChatService` interface unchanged. `ChatRequest`/`ChatStreamRequest`/`ChatResponse` DTOs gain `session_id`.
- **PrRag.Infrastructure**: `DependencyInjection.cs` registers the in-memory `IAgentSessionStore`.
- **PrRag.Api**: Minimal — no endpoint changes; `Program.cs` injects the same `IChatService`.
- **PrRag.Tests**: `FakeChatClient` streaming support and `IntegrationServiceFactory` registration of the session store need updates. Test assertions update to use `session_id` instead of reconstructing history.
- **Frontend**: Minimal — the chat page generates/persists a `session_id` and sends it on each request (history no longer needs to be resent).
- **Docker/CI**: No changes needed; MAF packages are NuGet-restored at build time.
