## MODIFIED Requirements

### Requirement: Layered agent composition
The system SHALL separate Microsoft Agent Framework usage into distinct, testable layers: (1) composition — agent identity and instructions defined in a dedicated `AgentInstructions`/`AgentSpec` type; (2) tools — the RAG/skill tools defined in a dedicated tools class with `[Description]`-annotated methods; (3) orchestration — a thin `IAgentRunService` called by the chat service, which itself owns only DTO mapping, session resolution, per-turn message assembly, and report writing; and (4) state — skill and per-turn state encapsulated in a dedicated helper over the session state bag.

#### Scenario: Agent composed away from a single orchestrator
- **WHEN** the application starts
- **THEN** the `AIAgent` is composed from the `AgentInstructions` identity + the tools class tool list via DI, and is not constructed inside `ChatService`

#### Scenario: Instructions resolvable standalone
- **WHEN** any component needs the agent name, description, or compiled system prompt
- **THEN** the `AgentInstructions` type provides them, including the dynamic skill-manifest section composed from `ISkillService`, without `ChatService` owning prompt text

#### Scenario: Tool list discoverable from the tools class
- **WHEN** the agent is composed
- **THEN** the tools class exposes the fixed five-tool list (`search_by_codes`, `search_semantic`, `activate_skill`, `create_requisition`, `get_suppliers_by_item`) built via `AIFunctionFactory`, each registered under its shared wire-name constant

## ADDED Requirements

### Requirement: Tool metadata declared once
The system SHALL declare each tool's description in exactly one place — the `[Description]` attribute on the handler method — and SHALL NOT restate it in a separate constant or pass it to the registration call. Each tool's wire name SHALL likewise be declared once as a shared constant consumed by both tool registration and the per-turn `RecordToolCall` bookkeeping, so a rename cannot silently diverge between the tool exposed to the model and the tool name written to the observability report.

#### Scenario: Handler description is the single source
- **WHEN** a tool is registered
- **THEN** its description is taken verbatim from the `[Description]` attribute on the handler method and is not overridden by a duplicated constant or an explicit registration-time value

#### Scenario: Wire name shared by registration and bookkeeping
- **WHEN** a tool is registered and later invoked
- **THEN** the name used to register the tool and the name recorded in the per-turn `ToolCalls` bookkeeping come from the same constant

#### Scenario: Every registered tool has a description
- **WHEN** the tools class exposes its tool list
- **THEN** every tool in the list has a non-empty description

### Requirement: Parameter descriptions reach the tool schema
The system SHALL bind each tool directly to its `[Description]`-annotated handler method so that the generated JSON schema sent to the chat model is derived from that method's parameters, and every non-cancellation-token parameter of a tool SHALL therefore carry a non-empty description in the generated schema. Parameters that are optional SHALL remain absent from the schema's required list.

#### Scenario: Parameter descriptions are present in the generated schema
- **WHEN** the tool list is built and a tool's generated JSON schema is inspected
- **THEN** every parameter of that tool other than the cancellation token has a non-empty `description` property

#### Scenario: Optional parameters stay optional
- **WHEN** a tool declares a parameter with a default value, such as the `items` and `suppliers` parameters of `search_by_codes`
- **THEN** that parameter is not listed in the generated schema's required array, and the tool remains callable with only one of the two supplied

#### Scenario: Forwarding lambdas are not used for registration
- **WHEN** a tool is registered
- **THEN** the registered delegate is the annotated handler method itself rather than a lambda that forwards to it, so the handler's parameter attributes are visible to schema generation
