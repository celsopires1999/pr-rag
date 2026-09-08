# Agent Framework Layering

## Purpose

Separate the Microsoft Agent Framework (MAF) integration into distinct, testable layers — composition (agent identity + instructions), tools, orchestration (run service + chat adapter), and session state — so no single class owns the whole agent lifecycle.

## Requirements

### Requirement: Layered agent composition
The system SHALL separate Microsoft Agent Framework usage into distinct, testable layers: (1) composition — agent identity and instructions defined in a dedicated `AgentInstructions`/`AgentSpec` type; (2) tools — the four RAG/skill tools defined in a dedicated tools class with `[Description]`-annotated methods; (3) orchestration — a thin `IAgentRunService` called by the chat service, which itself owns only DTO mapping, session resolution, per-turn message assembly, and report writing; and (4) state — skill and per-turn state encapsulated in a dedicated helper over the session state bag.

#### Scenario: Agent composed away from a single orchestrator
- **WHEN** the application starts
- **THEN** the `AIAgent` is composed from the `AgentInstructions` identity + the tools class tool list via DI, and is not constructed inside `ChatService`

#### Scenario: Instructions resolvable standalone
- **WHEN** any component needs the agent name, description, or compiled system prompt
- **THEN** the `AgentInstructions` type provides them, including the dynamic skill-manifest section composed from `ISkillService`, without `ChatService` owning prompt text

#### Scenario: Tool list discoverable from the tools class
- **WHEN** the agent is composed
- **THEN** the tools class exposes the fixed four-tool list (`search_by_codes`, `search_semantic`, `activate_skill`, `create_requisition`) built via `AIFunctionFactory`, each with a `[Description]` used verbatim in tool registration

### Requirement: Chat service as thin orchestration adapter
The system SHALL keep `ChatService` as a thin adapter that owns DTO mapping, `session_id` resolution, session get-or-create, per-turn message assembly (system prompt on first turn, active-skill body reinjection), calls to `IAgentRunService`, and RAG report writing — and SHALL NOT own agent construction, tool registration, prompt compilation, or inline skill-state key handling.

#### Scenario: Chat orchestration delegates to run service
- **WHEN** `ChatService.AnswerAsync` or `StreamAsync` is invoked
- **THEN** it resolves the session, assembles the turn messages, delegates the agent run to `IAgentRunService`, and writes the RAG report, with the same public response behavior as before

#### Scenario: Report still populated from turn context
- **WHEN** a chat request is answered
- **THEN** the RAG observability report is assembled from per-turn state (retrieved items, rewritten query, skill state) supplied through the new state helper or turn context, and written via `IRagReportWriter` unchanged

#### Scenario: Session skill state encapsulated
- **WHEN** skill state is read, injected, or cleared for a session
- **THEN** a dedicated state helper owns the state-bag keys and read/inject/clear logic, and the chat service and tools call that helper instead of inline magic keys