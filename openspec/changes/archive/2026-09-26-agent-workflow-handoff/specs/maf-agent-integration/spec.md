## MODIFIED Requirements

### Requirement: MAF agent registration
The system SHALL register the composed MAF agent via dependency injection by composing an orchestrator agent and its capability participants into a workflow graph and wrapping that graph with `WorkflowHostingExtensions.AsAIAgent(...)`, supplying the orchestrator's name, description, and compiled instructions from the dedicated `AgentInstructions`/`AgentSpec` type and each participant's instructions from its capability definition, and SHALL expose the composed agent to `ChatService` through the unchanged `IAgentRunService` abstraction instead of constructing the agent inside the chat service.

#### Scenario: Agent created from existing chat client at composition time
- **WHEN** the application starts and DI is configured
- **THEN** the system creates the orchestrator by wrapping the registered `IChatClient` via `AsAIAgent()`, with the orchestrator's name, description, and compiled instructions supplied by the `AgentInstructions`/`AgentSpec` type, rather than inline in `ChatService`

#### Scenario: Agent not constructed inside chat service
- **WHEN** `ChatService` is constructed
- **THEN** it receives the `IAgentRunService` (which wraps the composed agent) and does not call `AsAIAgent()` or hold a `ChatClientAgent` it built itself

#### Scenario: Compiled instructions are supplied to the agent rather than as a turn message
- **WHEN** the agent is composed
- **THEN** the compiled system prompt is supplied through the agent's instructions, and the chat service does not add it to the per-turn message list

#### Scenario: The workflow graph is exposed as a single agent
- **WHEN** the graph is composed
- **THEN** the orchestrator and its participants are presented to callers as one `AIAgent` obtained from the workflow, so `IAgentRunService`'s signatures and return types are unaffected by the number of participants

#### Scenario: The seam's shape is unchanged by the graph
- **WHEN** `IAgentRunService` is inspected
- **THEN** it declares the same session creation, run, and streaming operations with the same return types as before the graph was introduced

### Requirement: MAF tool registration
The system SHALL register the tool functions (`search_by_codes`, `search_semantic`, `activate_skill`, `get_suppliers_by_item`, `create_requisition_draft`, `confirm_requisition_draft`, `create_requisition`) with the agents that own them, taken from the capability catalog's per-capability tool lists rather than from its combined list, using `AIFunctionFactory.Create` on `[Description]`-annotated methods defined in per-capability units rather than delegates inlined in `ChatService`. Each tool's wire name SHALL come from a shared constant used by both its registration and its `RecordToolCall` entry, so the report cannot drift from the registered name.

#### Scenario: Tools bound to the agent that owns them
- **WHEN** the agents are composed in DI
- **THEN** the union of their tool lists is all seven tool functions, each bound to the agent whose capability owns it, so that `RunAsync`/`RunStreamingAsync` can invoke them during the agentic reasoning loop

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
- **WHEN** the agents are composed in DI
- **THEN** `get_suppliers_by_item` is among the registered tools and is callable by the agent that owns the retrieval capability to return the distinct suppliers for an item

#### Scenario: Tool set is unchanged by this grouping
- **WHEN** the union of the agents' tool lists is compared with the framework's tool set
- **THEN** the seven tools and their wire names are identical to the pre-grouping set, with none added, removed, or renamed

#### Scenario: Bookkeeping still works from a participant agent
- **WHEN** a participant agent invokes one of its tools during a turn
- **THEN** the invocation is recorded in the same per-turn bookkeeping that feeds the observability report, so the report is identical whether the orchestrator or a participant ran the tool

## ADDED Requirements

### Requirement: Workflow streaming surfaces participant responses
The system SHALL configure the workflow so that both whole agent responses and streaming response updates are surfaced from the graph, so that the server-sent-events chat path continues to receive per-token updates after a handoff rather than only a final result.

#### Scenario: Streaming updates reach the caller through the graph
- **WHEN** a client subscribes to a streaming chat request that is routed to a participant
- **THEN** it receives incremental response updates during the turn, not only a completed response at the end

#### Scenario: Response events are also surfaced
- **WHEN** a participant completes the work it was handed
- **THEN** its response is surfaced as an event from the graph so the turn's final answer can be produced and returned

#### Scenario: A graph that emits no updates is a failure
- **WHEN** the workflow is configured such that no response updates are emitted
- **THEN** a test fails, because a silent streaming regression would otherwise pass a suite that only exercises non-streaming answers
