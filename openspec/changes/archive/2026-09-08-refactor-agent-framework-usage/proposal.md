## Why

The existing Microsoft Agent Framework integration works but is not structurally sound. `ChatService` is a single ~436-line monolith that mixes agent creation (`ChatClientAgent`), tool registration via `AIFunctionFactory`, session management, skill state handling, RAG retrieval orchestration, observability reporting, and the compiled system prompt all in one class. Compared to a well-organized training example (the `AgentLab` sample for "Design, orchestrate, and scale agentic AI"), the project has no clear separation of agent composition from orchestration, no domain-layer agent/provider abstractions, and no use of MAF's `AIContextProvider`/hosting primitives. As this project becomes the base for a multi-agent implementation, this coupling will make agents impossible to reason about or extend independently.

## What Changes

- Extract the **agent definition** into dedicated files mirroring the `AgentLab` chapter-1 pattern: an `AgentInstructions` (agents naming + compiled/system prompt composition) and an `AgentSpec` (name, description, instructions) that live in the Application layer.
- Encapsulate the **four RAG/skill tools** (`search_by_codes`, `search_semantic`, `activate_skill`, `create_requisition`) into a dedicated tools class (`PurchaseRequisitionTools`), each as a named `[Description]`-annotated method wrapped via `AIFunctionFactory`, removed from the `ChatService` constructor.
- Introduce a **domain-agent abstraction** (`IAgentRunService` or similar) so `ChatService` becomes a thin adapter that owns only the request/response DTO mapping, session resolution, and report writing — not agent construction or tool wiring. **BREAKING** to internal wiring, contract unchanged externally.
- Move **session and skill state** handling off `ChatService`: session resolution stays in the chat service, but the skill-state read/inject/clear logic is encapsulated (either in the tools class or a small state helper) rather than inlined with magic-key constants.
- Move the **system prompt compilation** (including the dynamic skills manifest section) into the `AgentInstructions` composition layer, so prompt fragments are composable, not a single inline constant.
- Register the `IChatClient` → `AIAgent` composition in **DI** (Infrastructure) rather than inside `ChatService`, so the agent is a resolvable, testable component. Keep `ChatService` consuming a ready agent/run abstraction.
- Follow the `AgentLab` separation of concerns: **composition** (what the agent is: agent + instructions + tools), **orchestration** (per-turn run/session), and **memory/state** (session state bag) as distinct concerns with distinct files/types.
- Preserve the existing external API surface (`POST /api/chat`, `POST /api/chat/stream`, SSE format, session_id echo) and the RAG observability report, unchanged.

## Capabilities

### New Capabilities
- `agent-framework-layering`: Separation of concerns for the MAF integration — dedicated agent spec/instructions composition, a standalone tools class, a run abstraction, and DI-registered agent composition, mirroring the AgentLab structure.

### Modified Capabilities
- `maf-agent-integration`: Agent construction moves out of `ChatService` into DI/Infrastructure and a dedicated spec; agent identity, tools, and run surface remain the same but are now decomposable components.
- `agentic-retrieval`: Tool implementations/contracts unchanged, but tool registration moves from `ChatService` into a dedicated tools class with `[Description]`-annotated methods.
- `chat-query`: `ChatService` becomes a thin adapter over a composed agent/run abstraction; public contract and response format unchanged.
- `chat-streaming`: Streaming path routes through the same composed agent abstraction; SSE contract unchanged.
- `skill-framework`: Skill state read/inject/clear logic is encapsulated rather than inlined in `ChatService`; semantics unchanged.

## Impact

- **PrRag.Application**: Add agent spec/instructions and tools types; refactor `ChatService` into a thin orchestration adapter; introduce a run abstraction (interface + implementation). New files under `Services` (or an `Agents/` subfolder).
- **PrRag.Infrastructure**: `DependencyInjection.cs` composes and registers the `AIAgent` (or an `IAgentRunService`) instead of exposing only `IChatClient`; `ChatService` depends on the abstraction.
- **PrRag.Api**: Minimal. `Program.cs` continues to inject `IChatService`; no endpoint changes.
- **PrRag.Tests**: Fakes (`FakeChatClient`) and `IntegrationServiceFactory` may need to register the new composition/run abstraction; tool and session tests updated to the new types.
- **NuGet**: No new packages required; existing `Microsoft.Agents.AI`, `Microsoft.Extensions.AI(.OpenAI)` reused.
- **Frontend**: No changes — external contract unchanged.
- **Docker/CI**: No changes.
