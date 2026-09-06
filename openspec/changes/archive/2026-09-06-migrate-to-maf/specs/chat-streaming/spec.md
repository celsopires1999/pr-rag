## MODIFIED Requirements

### Requirement: Streamed chat responses
The system SHALL provide a streaming chat endpoint that returns the assistant answer incrementally via `AIAgent.RunStreamingAsync()` (or equivalent MAF streaming API), rather than only as a complete response.

#### Scenario: Receive answer incrementally
- **WHEN** a client calls the streaming chat endpoint with a question
- **THEN** the system begins streaming the answer tokens as they are produced via the MAF agent streaming API and signals completion at the end

#### Scenario: Same RAG grounding as non-streaming
- **WHEN** a client streams a question
- **THEN** the system uses the same MAF agent-driven tool retrieval and grounding pipeline as the non-streaming chat

### Requirement: Multi-turn conversation context
The system SHALL accept a `session_id` alongside the current question, resolve the corresponding server-side `AgentSession`, and include its accumulated history as context when generating the assistant answer, while still letting the agent resolve retrieval for the current turn.

#### Scenario: History included in the answer
- **WHEN** a client sends a current question with a `session_id` that has prior turns
- **THEN** the system generates the answer using the session's full conversation history as context together with any retrieved requisition context

#### Scenario: Retrieval resolved from current turn
- **WHEN** a client sends a new question within an ongoing session
- **THEN** the agent decides whether to invoke a PostgreSQL retrieval tool for the current turn, using the session's full history as conversation context
