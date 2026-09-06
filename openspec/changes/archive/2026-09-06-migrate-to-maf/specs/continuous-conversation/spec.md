## MODIFIED Requirements

### Requirement: Full conversation context to the model
The system SHALL pass conversation history to the `AIAgent` via a server-side `AgentSession` keyed by a client-supplied `session_id`, which manages the complete message history including all prior user/assistant messages and the current question on every turn.

#### Scenario: Prior turns included in the answer
- **WHEN** a client sends a current question with a `session_id` that has prior turns
- **THEN** the system resolves the existing `AgentSession` for that id and generates the answer using the session's accumulated conversation context

#### Scenario: New session behaves as single turn
- **WHEN** a client sends a first question with a `session_id` that has no prior turns
- **THEN** the system creates a new `AgentSession` with only the current question

### Requirement: Tool context persists across turns
The system SHALL use `AgentSession` to carry the results of tool calls into subsequent turns so follow-up questions can reference earlier retrieved requisitions without re-retrieving.

#### Scenario: Follow-up references earlier retrieval
- **WHEN** the agent retrieved requisitions in an earlier turn and the user asks a follow-up about them
- **THEN** the session preserves the prior tool results so the agent can answer the follow-up consistently

### Requirement: Conversation grounding without separate rewriter
The system SHALL use the full `AgentSession` conversation context for retrieval grounding on the current question, without requiring the client to repeat earlier questions.

#### Scenario: Current question grounds retrieval
- **WHEN** a client asks a follow-up question that depends on prior context
- **THEN** the agent resolves context from the ongoing session conversation and the current question's retrieval need
