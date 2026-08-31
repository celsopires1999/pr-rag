## ADDED Requirements

### Requirement: Chat interface
The web front-end SHALL provide a question input field that calls the existing `POST /api/chat` endpoint and renders the returned answer and retrieved-count to the user. The user SHALL be able to optionally set RAG parameters `top_k` and `min_similarity` before sending.

#### Scenario: Ask a question
- **WHEN** the user enters a question and submits it
- **THEN** the front-end calls `POST /api/chat` with the question and optional `top_k`/`min_similarity`
- **THEN** the front-end displays the answer and the number of retrieved documents

#### Scenario: Question is required
- **WHEN** the user submits an empty question
- **THEN** the front-end SHALL disable submission or show an inline error before calling the API

### Requirement: Ingestion trigger
The web front-end SHALL provide a control that calls the existing `POST /api/ingest` endpoint and displays the ingest result (total records, inserted, updated, embedded) to the user.

#### Scenario: Trigger ingestion
- **WHEN** the user clicks the ingest button
- **THEN** the front-end calls `POST /api/ingest`
- **THEN** the front-end displays the resulting inserted, updated and embedded counts

### Requirement: System status panel
The web front-end SHALL call the existing `GET /api/status` endpoint and display requisition count, embedded count, and last sync time. The front-end SHALL refresh this status periodically.

#### Scenario: View status
- **WHEN** the front-end loads (and on an automatic refresh interval)
- **THEN** the front-end calls `GET /api/status`
- **THEN** the front-end renders the requisition count, embedded count, and last sync time

### Requirement: API accessibility from the browser
The API host SHALL permit cross-origin requests from the configured web front-end origin so the browser-based UI can reach the API endpoints.

#### Scenario: Cross-origin chat request
- **WHEN** the front-end served from an allowed origin calls an API endpoint
- **THEN** the API responds with the appropriate CORS headers and handles the request

### Requirement: Containerized delivery
The front-end build SHALL be containerizable and runnable alongside the existing API in the `demo` Docker Compose profile.

#### Scenario: Run web container
- **WHEN** the web container is started via the `demo` profile
- **THEN** the UI is served and can reach the API service
