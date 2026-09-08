## MODIFIED Requirements

### Requirement: MAF agent registration
The system SHALL register the composed MAF agent via dependency injection, wrapping the existing `IChatClient` with the `AsAIAgent()` extension method using a `ChatClientAgentOptions` derived from a dedicated `AgentInstructions`/`AgentSpec` type (agent name, description, instructions, fixed tool list), and SHALL expose the composed agent to `ChatService` through an `IAgentRunService` abstraction instead of constructing the agent inside the chat service.

#### Scenario: Agent created from existing chat client at composition time
- **WHEN** the application starts and DI is configured
- **THEN** the system creates the agent by wrapping the registered `IChatClient` via `AsAIAgent()`, with the agent's name, description, and compiled instructions supplied by the `AgentInstructions`/`AgentSpec` type, rather than inline in `ChatService`

#### Scenario: Agent not constructed inside chat service
- **WHEN** `ChatService` is constructed
- **THEN** it receives the `IAgentRunService` (which wraps the composed agent) and does not call `AsAIAgent()` or hold a `ChatClientAgent` it built itself

### Requirement: MAF tool registration
The system SHALL register the four existing tool functions (`search_by_codes`, `search_semantic`, `activate_skill`, `create_requisition`) with the composed agent via its `Tools` property, using `AIFunctionFactory.Create` on `[Description]`-annotated methods defined in a dedicated tools class rather than delegates inlined in `ChatService`.

#### Scenario: Tools bound to agent
- **WHEN** the agent is composed in DI
- **THEN** it exposes the four tool functions so that `RunAsync`/`RunStreamingAsync` can invoke them during the agentic reasoning loop

#### Scenario: Tool implementations unchanged
- **WHEN** a tool is invoked during a chat request
- **THEN** the same repository, embedding, skill, and requisition-writer methods are called with the same parameters and return the same results as before, including per-turn `top_k`/`min_similarity` and retrieval bookkeeping for the observability report