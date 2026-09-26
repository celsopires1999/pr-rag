## MODIFIED Requirements

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

## ADDED Requirements

### Requirement: System prompt is recompiled per request
The system SHALL supply the compiled system prompt to the agent through the agent's instructions so that it is recomposed for every chat request, and SHALL NOT compose the skill manifest once per session, so that a skill catalog reloaded while a session is live reaches that session's subsequent requests.

#### Scenario: A skill reloaded mid-session reaches later turns
- **WHEN** the skills directory changes and the skill service reloads while a session with an active conversation is still in use
- **THEN** the next request in that session is answered against a prompt whose skill manifest reflects the reloaded catalog, rather than the manifest captured when the session was created

#### Scenario: A new session sees the current manifest
- **WHEN** a session is created after a skill catalog reload
- **THEN** its compiled prompt lists the reloaded skills, as it did before this change

#### Scenario: The prompt is not duplicated across turns of a session
- **WHEN** a second turn of the same session is sent to the chat model
- **THEN** the messages the model receives contain the skill manifest section exactly once, so the framework does not append an additional copy of the instructions on every turn

#### Scenario: Per-turn message assembly is unaffected
- **WHEN** a turn is assembled for a request
- **THEN** the current user message and any not-yet-injected active-skill guidance are still supplied as turn messages, so skill guidance continues to be injected exactly once per activation
