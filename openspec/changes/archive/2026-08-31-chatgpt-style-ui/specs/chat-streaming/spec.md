## ADDED Requirements

### Requirement: Streamed chat responses
The system SHALL provide a streaming chat endpoint that returns the assistant answer incrementally as it is generated, rather than only as a complete response.

#### Scenario: Receive answer incrementally
- **WHEN** a client calls the streaming chat endpoint with a question
- **THEN** the system begins streaming the answer tokens as they are produced and signals completion at the end

#### Scenario: Same RAG grounding as non-streaming
- **WHEN** a client streams a question
- **THEN** the system uses the same retrieval and grounding pipeline as the non-streaming chat

### Requirement: Multi-turn conversation context
The system SHALL accept a conversation history alongside the current question and SHALL include that history as context when generating the assistant answer, while still grounding on retrieval performed for the current question.

#### Scenario: History included in the answer
- **WHEN** a client sends a current question together with prior user/assistant messages
- **THEN** the system generates the answer using the conversation history as context together with the retrieved requisition context

#### Scenario: Retrieval scoped to current question
- **WHEN** a client sends history with a new question
- **THEN** the system performs retrieval based on the current question, not the aggregated history

## MODIFIED Requirements

### Requirement: Non-streaming chat responses
The system SHALL return chat answers as a complete response rather than streaming tokens.

#### Scenario: Full response returned
- **WHEN** a client calls `POST /api/chat`
- **THEN** the system returns the complete answer in a single response body
