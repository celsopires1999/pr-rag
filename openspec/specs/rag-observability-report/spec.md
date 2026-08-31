# RAG Observability Report

## Purpose

Per-request observability tracing for the RAG chat pipeline, capturing question-to-answer traces and persisting them as machine-readable report files for debugging and analysis.

## Requirements

### Requirement: Per-request RAG observability report
The system SHALL capture a per-request observability trace for each answered chat question, covering the full question-to-answer pipeline, and SHALL persist it as a machine-readable file in a local, non-committed output directory. The report SHALL include the timestamp, the original question, the effective `top_k` and `min_similarity` (noting whether each came from the request or the configured defaults), the rewritten query when vector search is performed, the retrieved requisitions with their similarity scores, and the final answer sent to the user.

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

### Requirement: Report files not committed
The generated report files SHALL be written outside version control so that they are never committed with the source code, while the code that produces them remains tracked.

#### Scenario: Reports excluded from git
- **WHEN** reports are generated in the configured output directory
- **THEN** the directory is excluded by `.gitignore` and report files do not appear in commits
