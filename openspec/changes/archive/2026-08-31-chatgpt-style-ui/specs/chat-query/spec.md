## ADDED Requirements

### Requirement: Non-streaming endpoint remains the compatibility path
The `POST /api/chat` endpoint SHALL continue to return complete, non-streaming responses as the compatibility path, with streaming available via a separate endpoint.

#### Scenario: Non-streaming endpoint unaffected
- **WHEN** a client calls `POST /api/chat`
- **THEN** the endpoint returns the complete answer in a single response body as before
