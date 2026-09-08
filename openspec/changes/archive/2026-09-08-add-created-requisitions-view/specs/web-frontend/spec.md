# Web Front-end

## MODIFIED Requirements

### Requirement: Left sidebar with navigation and system options
The front-end SHALL present a persistent left sidebar containing navigation links between the Chat, Requisitions, and System Status pages, and a bottom section with RAG parameters (`top_k`, `min_similarity`) and an ingestion trigger.

#### Scenario: Navigate between pages
- **WHEN** the user clicks a navigation link in the sidebar
- **THEN** the application navigates to the corresponding page while keeping the sidebar visible

#### Scenario: Change RAG parameters
- **WHEN** the user edits `top_k` or `min_similarity` in the sidebar
- **THEN** subsequent chat requests use the updated values

#### Scenario: Trigger ingestion from sidebar
- **WHEN** the user clicks the ingest control in the sidebar
- **THEN** the application calls the ingest endpoint and shows the result