## MODIFIED Requirements

### Requirement: Layered agent composition
The system SHALL separate Microsoft Agent Framework usage into distinct, testable layers: (1) composition — agent identity and instructions defined in a dedicated `AgentInstructions`/`AgentSpec` type, plus the workflow graph that joins the orchestrator to its capability agents; (2) capabilities — the RAG/skill tools grouped into per-capability units, each declaring its own `[Description]`-annotated methods and the prompt fragment documenting them, and each bindable to its own agent; (3) orchestration — a thin `IAgentRunService` called by the chat service, which itself owns only DTO mapping, session resolution, per-turn message assembly, and report writing; and (4) state — skill and per-turn state encapsulated in a dedicated helper over the session state bag.

#### Scenario: Agent composed away from a single orchestrator
- **WHEN** the application starts
- **THEN** the agents are composed from the `AgentInstructions` identity + the capability catalog's per-capability definitions via DI, into a workflow graph that is exposed as one `AIAgent`, and are not constructed inside `ChatService`

#### Scenario: Instructions resolvable standalone
- **WHEN** any component needs the agent name, description, or compiled system prompt
- **THEN** the `AgentInstructions` type provides them, including the dynamic skill-manifest section composed from `ISkillService` and the action blocks contributed by each capability, without `ChatService` owning prompt text

#### Scenario: Tool list discoverable from the capability catalog
- **WHEN** the agents are composed
- **THEN** the capability catalog exposes the per-capability tool lists — `search_by_codes`, `search_semantic`, `activate_skill`, `get_suppliers_by_item`, `create_requisition_draft`, `confirm_requisition_draft`, `create_requisition` — built via `AIFunctionFactory` from the `[Description]`-annotated methods of the capability units, each registered under its shared wire-name constant, with each tool assigned to the one agent whose capability owns it

#### Scenario: Per-capability prompt text is discoverable with its tools
- **WHEN** any component needs the prompt text documenting a particular tool
- **THEN** it is obtained from the action block of the capability unit that registers that tool, rather than from a single monolithic instruction block

#### Scenario: The graph is composed in its own layer
- **WHEN** any component needs to know how the orchestrator and its participants are joined
- **THEN** it is described by the dedicated composition type, and the run service neither builds the graph nor enumerates its participants

### Requirement: Chat service as thin orchestration adapter
The system SHALL keep `ChatService` as a thin adapter that owns DTO mapping, `session_id` resolution, session get-or-create, per-turn message assembly (active-skill body reinjection), calls to `IAgentRunService`, and RAG report writing — and SHALL NOT own agent construction, workflow composition, tool registration, prompt compilation, the system prompt's placement in the message list, routing between agents, or inline skill-state key handling.

#### Scenario: Chat orchestration delegates to run service
- **WHEN** `ChatService.AnswerAsync` or `StreamAsync` is invoked
- **THEN** it resolves the session, assembles the turn messages, delegates the agent run to `IAgentRunService`, and writes the RAG report, with the same public response behavior as before

#### Scenario: Report still populated from turn context
- **WHEN** a chat request is answered
- **THEN** the RAG observability report is assembled from per-turn state (retrieved items, rewritten query, skill state) supplied through the new state helper or turn context, and written via `IRagReportWriter` unchanged

#### Scenario: Session skill state encapsulated
- **WHEN** skill state is read, injected, or cleared for a session
- **THEN** a dedicated state helper owns the state-bag keys and read/inject/clear logic, and the chat service and tools call that helper instead of inline magic keys

#### Scenario: Chat service does not emit the system prompt
- **WHEN** the chat service assembles the messages for a turn
- **THEN** it does not add the compiled system prompt to the message list, because the prompt is supplied through the agent's own instructions rather than injected as a turn message

#### Scenario: Chat service does not route between agents
- **WHEN** a turn is handed to the composed agent
- **THEN** the chat service selects no agent, performs no handoff, and is unaware of how many agents the graph contains

## ADDED Requirements

### Requirement: Adopting a multi-agent graph does not re-plumb the application
The system SHALL introduce agent routing behind the existing run-service seam, so that the chat service, HTTP endpoints, streaming path, and session store require no change when capabilities are extracted into their own agents.

#### Scenario: The public chat surface is unchanged by an extraction
- **WHEN** a capability is extracted from the orchestrator into its own agent
- **THEN** no change is required in `ChatService`, the request or response contracts, the streaming endpoint, or the session store

#### Scenario: The seam is the only thing that knows the graph exists
- **WHEN** the number of agents behind the seam changes
- **THEN** the change is confined to the composition and run-service types, and the chat orchestrator continues to call the same three seam operations
