## MODIFIED Requirements

### Requirement: Per-request RAG observability report
The system SHALL capture a per-request observability trace for each answered chat question, covering the full question-to-answer pipeline, and SHALL persist it as a machine-readable file in a local, non-committed output directory. The report SHALL include the timestamp, the original question, the effective `top_k` and `min_similarity` (noting whether each came from the request or the configured defaults), the rewritten query when vector search is performed, the retrieved requisitions with their similarity scores, the tools invoked during the turn together with their arguments, and the final answer sent to the user.

#### Scenario: Report generated for a grounded answer
- **WHEN** a client sends `POST /api/chat` with a question and the system produces an answer
- **THEN** the system writes a report file containing the timestamp, original question, effective `top_k` and `min_similarity`, retrieved requisitions with similarity scores, and the final answer

#### Scenario: Report records effective parameters
- **WHEN** the client omits `top_k` or `min_similarity`
- **THEN** the report records the configured default value for each omitted parameter and indicates it came from defaults

#### Scenario: Report captures rewritten query
- **WHEN** vector search is performed after the question is rewritten
- **THEN** the report records the rewritten query alongside the original question

#### Scenario: Report captures no-context fallback
- **WHEN** no relevant context is found and the system returns the fallback answer
- **THEN** the report records the empty retrieval and the fallback answer sent to the user

#### Scenario: Report records tools invoked during the turn
- **WHEN** the agent invokes one or more tools (e.g. `search_semantic`, `search_by_codes`, `activate_skill`, `create_requisition`) to produce the answer
- **THEN** the report records each tool invocation with its name and arguments in the order they were called

#### Scenario: Report shows no tools when none are invoked
- **WHEN** the answer is produced without invoking any tool
- **THEN** the report records an empty tool call list