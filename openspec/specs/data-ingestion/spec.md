# Data Ingestion

## Purpose

Ingest purchase requisitions from a bind-mounted JSON file, upserting incrementally and generating embeddings for new or changed rows, with automatic and manual triggers and observable status.

## Requirements

### Requirement: Incremental ingestion from JSON file
The system SHALL ingest purchase requisitions from a bind-mounted JSON file, upserting by `purchase_requisition` and only re-embedding rows that are new or changed.

#### Scenario: Initial import
- **WHEN** a JSON file is first provided
- **THEN** the system stores all requisitions and generates an embedding for each

#### Scenario: Re-import with no changes
- **WHEN** the same JSON content is imported again
- **THEN** the system does not re-embed existing unchanged rows

#### Scenario: Re-import with changed rows
- **WHEN** a JSON file is imported where some existing `purchase_requisition` records have changed fields (or new records were added)
- **THEN** the system overwrites the relational fields of existing records, inserts new records, and regenerates embeddings only for changed/new rows

#### Scenario: Upsert overwrites existing row
- **WHEN** an imported `purchase_requisition` already exists
- **THEN** the system overwrites all of its fields with the new values

### Requirement: Embedding source composition
The system SHALL generate each requisition's embedding from the concatenation of `supplier_name`, `item_name`, and `description`.

#### Scenario: Concatenated embedding source
- **WHEN** generating an embedding for a requisition
- **THEN** the text embedded is the concatenation of its supplier name, item name, and description

### Requirement: Automatic and manual ingestion triggers
The system SHALL trigger ingestion automatically when the bind-mounted JSON file changes, and SHALL also provide a manual endpoint to force re-import.

#### Scenario: Automatic trigger on file change
- **WHEN** the bind-mounted JSON file is modified
- **THEN** the system automatically runs ingestion

#### Scenario: Manual trigger
- **WHEN** a client calls `POST /api/ingest`
- **THEN** the system runs ingestion against the current JSON file regardless of file-change detection

### Requirement: Ingestion observability
The system SHALL expose status indicating the number of requisitions stored, the number embedded, and the last sync time.

#### Scenario: Status endpoint reflects state
- **WHEN** a client queries the status endpoint
- **THEN** it returns the stored requisition count, embedded count, and last synchronization timestamp
