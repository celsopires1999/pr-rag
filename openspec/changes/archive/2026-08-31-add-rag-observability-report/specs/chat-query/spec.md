## ADDED Requirements

### Requirement: Chat answers emit observability records
The chat answer pipeline SHALL additionally emit an observability trace for each answered question, without changing the answer content or the public `POST /api/chat` response body.

#### Scenario: Answer pipeline emits a trace
- **WHEN** `POST /api/chat` is answered
- **THEN** the pipeline records the question-to-answer trace for observability while returning the unchanged answer in the response body

#### Scenario: Public response contract unchanged
- **WHEN** a client sends `POST /api/chat`
- **THEN** the response body remains identical to the behavior without observability, and report generation has no effect on the response
