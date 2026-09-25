## MODIFIED Requirements

### Requirement: MAF tool registration
The system SHALL register the tool functions (`search_by_codes`, `search_semantic`, `activate_skill`, `get_suppliers_by_item`, `create_requisition_draft`, `confirm_requisition_draft`, `create_requisition`) with the composed agent via its `Tools` property, using `AIFunctionFactory.Create` on `[Description]`-annotated methods defined in a dedicated tools class rather than delegates inlined in `ChatService`. Each tool's wire name SHALL come from a shared constant used by both its registration and its `RecordToolCall` entry, so the report cannot drift from the registered name.

#### Scenario: Tools bound to agent
- **WHEN** the agent is composed in DI
- **THEN** it exposes all seven tool functions so that `RunAsync`/`RunStreamingAsync` can invoke them during the agentic reasoning loop

#### Scenario: Handler methods registered directly
- **WHEN** a tool is registered
- **THEN** the handler method itself is passed to `AIFunctionFactory.Create` rather than a forwarding lambda, so every parameter's `[Description]` reaches the model's JSON schema

#### Scenario: Tool implementations unchanged
- **WHEN** a tool is invoked during a chat request
- **THEN** the same repository, embedding, skill, and requisition-writer methods are called with the same parameters and return the same results as before, including per-turn `top_k`/`min_similarity` and retrieval bookkeeping for the observability report

#### Scenario: Recorded name matches the registered wire name
- **WHEN** any tool is invoked during a turn
- **THEN** the name recorded in the observability report is the same wire name the model was given for that tool

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

## ADDED Requirements

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
