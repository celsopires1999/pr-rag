# Chat Streaming

Delta for the `chat-streaming` capability reflecting the move to agentic tool-driven retrieval for streamed answers.

## MODIFIED Requirements

### Requirement: Streamed chat responses
The system SHALL provide a streaming chat endpoint that returns the assistant answer incrementally as it is generated, rather than only as a complete response.

#### Scenario: Receive answer incrementally
- **WHEN** a client calls the streaming chat endpoint with a question
- **THEN** the system begins streaming the answer tokens as they are produced and signals completion at the end

#### Scenario: Same RAG grounding as non-streaming
- **WHEN** a client streams a question
- **THEN** the system uses the same tool-driven retrieval and grounding pipeline as the non-streaming chat

### Requirement: Multi-turn conversation context
The system SHALL accept a complete conversation history alongside the current question and SHALL include that history as context when generating the assistant answer, while still letting the model resolve retrieval for the current turn.

#### Scenario: History included in the answer
- **WHEN** a client sends a current question together with prior user/assistant messages
- **THEN** the system generates the answer using the full conversation history as context together with any retrieved requisition context

#### Scenario: Retrieval resolved from current turn
- **WHEN** a client sends history with a new question
- **THEN** the system lets the model decide whether to invoke a PostgreSQL retrieval tool for the current turn, using the full history as conversation context
