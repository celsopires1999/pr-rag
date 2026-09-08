## MODIFIED Requirements

### Requirement: Streamed chat responses
The system SHALL provide a streaming chat endpoint that returns the assistant answer incrementally via the same composed MAF agent execution surface used for non-streaming chat (an `IAgentRunService` exposing `RunStreamingAsync`), rather than only as a complete response.

#### Scenario: Receive answer incrementally
- **WHEN** a client calls the streaming chat endpoint with a question
- **THEN** the system begins streaming the answer tokens as they are produced via the composed agent's streaming API and signals completion at the end

#### Scenario: Same RAG grounding as non-streaming
- **WHEN** a client streams a question
- **THEN** the system uses the same agent-driven tool retrieval and grounding pipeline as the non-streaming chat