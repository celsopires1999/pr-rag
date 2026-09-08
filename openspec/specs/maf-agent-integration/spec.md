# MAF Agent Integration

## Purpose

Integrate the Microsoft Agent Framework (MAF) for agentic chat processing, session-based multi-turn state, and tool registration.

## Requirements

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

### Requirement: Agent session for multi-turn state, keyed by session id
The system SHALL use a server-side `AgentSession` (managed by an in-memory store keyed by a client-supplied `session_id`) to manage conversation history and tool-call state across turns, replacing manual message reconstruction.

#### Scenario: Session stored by session id
- **WHEN** a chat request includes a `session_id`
- **THEN** the system resolves the matching `AgentSession` from the in-memory store, creating one if it does not exist, and passes it to the agent for processing

#### Scenario: Session accumulates history server-side
- **WHEN** multiple chat requests use the same `session_id`
- **THEN** the `AgentSession` accumulates the full message history across runs, so the client does not need to resend the history each turn

#### Scenario: Session eliminates manual history rebuild
- **WHEN** a chat request is processed
- **THEN** the system does not reconstruct the message list from client history — the session handles history management

### Requirement: Observability report written post-hoc
The system SHALL continue to assemble and write `RagQueryReport` records after each chat answer via `WriteReportAsync` in `ChatService`, without changing the answer content or the public response body.

#### Scenario: Report emitted on chat answer
- **WHEN** the agent produces a final answer
- **THEN** `ChatService` captures the question, rewritten query, retrieved items, skill state, and answer, and writes a `RagQueryReport` via `IRagReportWriter`

#### Scenario: Report does not alter answer content
- **WHEN** a response is produced
- **THEN** the answer returned to the caller is identical to the behavior without report generation

### Requirement: Session-based skill state
The system SHALL store the active skill name and body in the `AgentSession` state bag rather than using the `[Skill: name]` regex marker prefix in assistant messages.

#### Scenario: Skill stored in session state
- **WHEN** the `activate_skill` tool is invoked and a skill is found
- **THEN** the skill name and body are stored in the active session's state bag

#### Scenario: Skill restored from session, not regex
- **WHEN** a follow-up turn occurs in the same session and a skill was active in a previous turn
- **THEN** the system restores the skill from the session state bag rather than parsing a `[Skill: name]` marker from a previous message

#### Scenario: Skill marker removed from answers
- **WHEN** a skill is active and the agent produces an answer
- **THEN** the answer does not contain the `[Skill: name]` prefix — the session state bag handles skill persistence
