# Web Front-end

## Purpose

Provide a browser front-end for interacting with the RAG system: a persistent left sidebar for navigation and system options, a ChatGPT-style chat page with streaming answers, and a separate system status page.

## Requirements

### Requirement: Left sidebar with navigation and system options
The front-end SHALL present a persistent left sidebar containing navigation links between the Chat and System Status pages, and a bottom section with RAG parameters (`top_k`, `min_similarity`) and an ingestion trigger.

#### Scenario: Navigate between pages
- **WHEN** the user clicks the System Status link in the sidebar
- **THEN** the application navigates to the System Status page while keeping the sidebar visible

#### Scenario: Change RAG parameters
- **WHEN** the user edits `top_k` or `min_similarity` in the sidebar
- **THEN** subsequent chat requests use the updated values

#### Scenario: Trigger ingestion from sidebar
- **WHEN** the user clicks the ingest control in the sidebar
- **THEN** the application calls the ingest endpoint and shows the result

### Requirement: ChatGPT-style chat page
The front-end SHALL render the Chat page as a message list with a prominent input at the bottom, matching the familiar ChatGPT layout, and SHALL stream assistant answers incrementally and render them as Markdown-formatted content as they arrive.

#### Scenario: Stream assistant answer
- **WHEN** the user submits a question
- **THEN** the application displays the user message and streams the assistant reply token-by-token into the message list, rendering Markdown formatting (code blocks, bold, lists, tables, links) in real time

#### Scenario: Multi-turn in session
- **WHEN** a conversation already has prior user/assistant messages
- **THEN** the current question is sent together with the prior history so the assistant has conversation context

### Requirement: Separate system status page
The front-end SHALL show ingestion status (requisition count, embedded count, last sync) on its own page, reachable from the sidebar.

#### Scenario: View status page
- **WHEN** the user navigates to the System Status page
- **THEN** the application renders the requisition/embedded counts and last-sync time, refreshing automatically

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
