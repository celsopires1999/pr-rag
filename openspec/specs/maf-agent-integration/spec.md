# MAF Agent Integration

## Purpose

Integrate the Microsoft Agent Framework (MAF) for agentic chat processing, session-based multi-turn state, and tool registration.

## Requirements

### Requirement: MAF agent registration
The system SHALL register the composed MAF agent via dependency injection, wrapping the existing `IChatClient` with the `AsAIAgent()` extension method and supplying the agent's name, description, and compiled instructions from a dedicated `AgentInstructions`/`AgentSpec` type, together with the tool list contributed by the capability catalog, and SHALL expose the composed agent to `ChatService` through an `IAgentRunService` abstraction instead of constructing the agent inside the chat service.

#### Scenario: Agent created from existing chat client at composition time
- **WHEN** the application starts and DI is configured
- **THEN** the system creates the agent by wrapping the registered `IChatClient` via `AsAIAgent()`, with the agent's name, description, and compiled instructions supplied by the `AgentInstructions`/`AgentSpec` type, rather than inline in `ChatService`

#### Scenario: Agent not constructed inside chat service
- **WHEN** `ChatService` is constructed
- **THEN** it receives the `IAgentRunService` (which wraps the composed agent) and does not call `AsAIAgent()` or hold a `ChatClientAgent` it built itself

#### Scenario: Compiled instructions are supplied to the agent rather than as a turn message
- **WHEN** the agent is composed
- **THEN** the compiled system prompt is supplied through the agent's instructions, and the chat service does not add it to the per-turn message list

### Requirement: MAF tool registration
The system SHALL register the tool functions (`search_by_codes`, `search_semantic`, `activate_skill`, `get_suppliers_by_item`, `create_requisition_draft`, `confirm_requisition_draft`, `create_requisition`) with the composed agent from the capability catalog's combined tool list, using `AIFunctionFactory.Create` on `[Description]`-annotated methods defined in per-capability units rather than delegates inlined in `ChatService`. Each tool's wire name SHALL come from a shared constant used by both its registration and its `RecordToolCall` entry, so the report cannot drift from the registered name.

#### Scenario: Tools bound to agent
- **WHEN** the agent is composed in DI
- **THEN** it exposes all seven tool functions, gathered from the capability catalog, so that `RunAsync`/`RunStreamingAsync` can invoke them during the agentic reasoning loop

#### Scenario: Handler methods registered directly
- **WHEN** a tool is registered
- **THEN** the handler method itself is passed to `AIFunctionFactory.Create` rather than a forwarding lambda, so every parameter's `[Description]` reaches the model's JSON schema

#### Scenario: Tool implementations unchanged
- **WHEN** a tool is invoked during a chat request
- **THEN** the same repository, embedding, skill, and requisition-writer methods are called with the same parameters and return the same results as before, including per-turn `top_k`/`min_similarity` and retrieval bookkeeping for the observability report

#### Scenario: Recorded name matches the registered wire name
- **WHEN** any tool is invoked during a turn
- **THEN** the name recorded in the observability report is the same wire name the model was given for that tool

#### Scenario: Item supplier lookup tool registered
- **WHEN** the agent is composed in DI
- **THEN** `get_suppliers_by_item` is among the registered tools and is callable by the agent to return the distinct suppliers for an item

#### Scenario: Tool set is unchanged by this grouping
- **WHEN** the capability catalog's combined tool list is compared with the framework's tool set
- **THEN** the seven tools and their wire names are identical to the pre-grouping set, with none added, removed, or renamed

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
The system SHALL continue to assemble and write `RagQueryReport` records after each chat answer via `WriteReportAsync` in `ChatService`, without changing the answer content or the public response body. The report SHALL additionally record the requisition confirmation path for the turn, distinguishing a confirmed and persisted creation from a refused one.

#### Scenario: Report emitted on chat answer
- **WHEN** the agent produces a final answer
- **THEN** `ChatService` captures the question, rewritten query, retrieved items, skill state, confirmation path, and answer, and writes a `RagQueryReport` via `IRagReportWriter`

#### Scenario: Report does not alter answer content
- **WHEN** a response is produced
- **THEN** the answer returned to the caller is identical to the behavior without report generation

#### Scenario: Existing report fields unchanged
- **WHEN** a report is written for a turn that performs no requisition activity
- **THEN** every pre-existing report field carries the same value it did before this change, and the new confirmation-path fields are empty

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

### Requirement: Session-based requisition draft state
The system SHALL store the requisition draft, its presented flag, and its confirmed flag in the `AgentSession` state bag, owned by a dedicated helper alongside the existing skill state helper, and SHALL clear all three after a successful creation so that a second creation in the same session is refused.

#### Scenario: Draft state stored in the session bag
- **WHEN** the model stages, presents, and confirms a requisition draft
- **THEN** the draft values and both flags are stored in the active session's state bag rather than in Postgres or in a server-side singleton

#### Scenario: Draft survives across turns of the session
- **WHEN** a draft is staged in one turn and the user confirms in a later turn of the same session
- **THEN** the system restores the draft from the state bag rather than asking the model to restate every field

#### Scenario: Draft discarded when the session ends
- **WHEN** a session ends with an unconfirmed draft
- **THEN** nothing remains in the database, because drafts exist only in session state

#### Scenario: Creation clears draft state
- **WHEN** a requisition is persisted from a confirmed draft
- **THEN** the draft, its presented flag, and its confirmed flag are all removed from the session state bag

### Requirement: Typed tool result contracts
The system SHALL return tool results as dedicated result DTOs whose properties carry explicit camelCase JSON names, and SHALL let the AI function layer JSON-serialize those DTOs rather than passing raw domain, report, or repository objects through to the chat model. Result DTOs SHALL be decoupled from the RAG observability report schema so that report consumers are unaffected by changes to the model-facing payload.

#### Scenario: Search tool returns a counted result object
- **WHEN** the chat model calls `search_by_codes` or `search_semantic`
- **THEN** the tool result is a JSON object containing a result count and an items array of requisition hits with camelCase keys, rather than a bare array of framework objects

#### Scenario: Supplier lookup returns a counted result object
- **WHEN** the chat model calls `get_suppliers_by_item`
- **THEN** the tool result is a JSON object containing a supplier count and a suppliers array of `{supplierCode, supplierName}` pairs

#### Scenario: Skill activation returns a structured result
- **WHEN** the chat model calls `activate_skill`
- **THEN** the tool result is a JSON object carrying the requested skill name, an explicit found flag, the guidance body when the skill exists, and a human-readable message in both the found and not-found cases

#### Scenario: Requisition creation returns a structured result
- **WHEN** the chat model calls `create_requisition`
- **THEN** the tool result is a JSON object carrying an explicit success flag, the created requisition id when the write succeeded, and a human-readable message describing the outcome

#### Scenario: Empty results are unambiguous
- **WHEN** a search or lookup tool finds no matching records
- **THEN** the result object reports a count of zero with an empty array, so the model can distinguish "no matches" from a malformed response

#### Scenario: Observability report schema is unaffected
- **WHEN** the RAG observability report is written for a turn whose tools returned the new typed results
- **THEN** the report's retrieved-item and tool-call fields keep their existing shape and property naming

### Requirement: System prompt is recompiled per request
The system SHALL supply the compiled system prompt to the agent through the agent's instructions so that it is recomposed for every chat request, and SHALL NOT compose the skill manifest once per session, so that a skill catalog reloaded while a session is live reaches that session's subsequent requests.

#### Scenario: A skill reloaded mid-session reaches later turns
- **WHEN** the skills directory changes and the skill service reloads while a session with an active conversation is still in use
- **THEN** the next request in that session is answered against a prompt whose skill manifest reflects the reloaded catalog, rather than the manifest captured when the session was created

#### Scenario: A new session sees the current manifest
- **WHEN** a session is created after a skill catalog reload
- **THEN** its compiled prompt lists the reloaded skills, as it did before this change

#### Scenario: The instructions channel does not accumulate the prompt across turns
- **WHEN** a second turn of the same session is sent to the chat model
- **THEN** the skill manifest is still supplied exactly once, through the agent's instructions on that request, and is not additionally present in the message history, so the framework does not append another copy per turn

#### Scenario: Per-turn message assembly is unaffected
- **WHEN** a turn is assembled for a request
- **THEN** the current user message and any not-yet-injected active-skill guidance are still supplied as turn messages, so skill guidance continues to be injected exactly once per activation
