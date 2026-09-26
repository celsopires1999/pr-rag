## MODIFIED Requirements

### Requirement: Layered agent composition
The system SHALL separate Microsoft Agent Framework usage into distinct, testable layers: (1) composition — agent identity and instructions defined in a dedicated `AgentInstructions`/`AgentSpec` type; (2) capabilities — the RAG/skill tools grouped into per-capability units, each declaring its own `[Description]`-annotated methods and the prompt fragment documenting them; (3) orchestration — a thin `IAgentRunService` called by the chat service, which itself owns only DTO mapping, session resolution, per-turn message assembly, and report writing; and (4) state — skill and per-turn state encapsulated in a dedicated helper over the session state bag.

#### Scenario: Agent composed away from a single orchestrator
- **WHEN** the application starts
- **THEN** the `AIAgent` is composed from the `AgentInstructions` identity + the capability catalog's combined tool list via DI, and is not constructed inside `ChatService`

#### Scenario: Instructions resolvable standalone
- **WHEN** any component needs the agent name, description, or compiled system prompt
- **THEN** the `AgentInstructions` type provides them, including the dynamic skill-manifest section composed from `ISkillService` and the action blocks contributed by each capability, without `ChatService` owning prompt text

#### Scenario: Tool list discoverable from the capability catalog
- **WHEN** the agent is composed
- **THEN** the capability catalog exposes the combined tool list — `search_by_codes`, `search_semantic`, `activate_skill`, `get_suppliers_by_item`, `create_requisition_draft`, `confirm_requisition_draft`, `create_requisition` — built via `AIFunctionFactory` from the `[Description]`-annotated methods of the capability units, each registered under its shared wire-name constant

#### Scenario: Per-capability prompt text is discoverable with its tools
- **WHEN** any component needs the prompt text documenting a particular tool
- **THEN** it is obtained from the action block of the capability unit that registers that tool, rather than from a single monolithic instruction block

### Requirement: Chat service as thin orchestration adapter
The system SHALL keep `ChatService` as a thin adapter that owns DTO mapping, `session_id` resolution, session get-or-create, per-turn message assembly (active-skill body reinjection), calls to `IAgentRunService`, and RAG report writing — and SHALL NOT own agent construction, tool registration, prompt compilation, the system prompt's placement in the message list, or inline skill-state key handling.

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

## ADDED Requirements

### Requirement: Wire names shared across capability units
The system SHALL declare every tool's wire name exactly once in a single shared type consumed by both the tool's registration and its per-turn `RecordToolCall` bookkeeping, and SHALL NOT declare a wire name on an individual capability unit, so that a rename remains a single edit and cannot diverge between the tool exposed to the model and the tool name written to the observability report.

#### Scenario: Registration and bookkeeping read the same declaration
- **WHEN** a tool is registered by a capability unit and later invoked
- **THEN** both the name the model was given and the name recorded for the observability report come from the same shared declaration

#### Scenario: Wire names are not owned by a capability unit
- **WHEN** a capability unit is inspected for its tool names
- **THEN** the names are referenced from the shared type rather than declared on that unit, so two units cannot claim the same name
