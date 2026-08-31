## ADDED Requirements

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
The front-end SHALL render the Chat page as a message list with a prominent input at the bottom, matching the familiar ChatGPT layout, and SHALL stream assistant answers incrementally and render them as they arrive.

#### Scenario: Stream assistant answer
- **WHEN** the user submits a question
- **THEN** the application displays the user message and streams the assistant reply token-by-token into the message list

#### Scenario: Multi-turn in session
- **WHEN** a conversation already has prior user/assistant messages
- **THEN** the current question is sent together with the prior history so the assistant has conversation context

### Requirement: Separate system status page
The front-end SHALL show ingestion status (requisition count, embedded count, last sync) on its own page, reachable from the sidebar.

#### Scenario: View status page
- **WHEN** the user navigates to the System Status page
- **THEN** the application renders the requisition/embedded counts and last-sync time, refreshing automatically
