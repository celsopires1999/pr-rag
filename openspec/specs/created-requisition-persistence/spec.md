# Created Requisition Persistence

## Purpose

Persists purchase requisitions created by the assistant into a dedicated Postgres schema, separate from the schema hosting the historical `purchase_requisitions` data, so that created requisitions never mix with the ingested history and historical master data stays untouched.

## Requirements

### Requirement: Created requisitions persisted in a dedicated schema
The system SHALL store purchase requisitions created by the assistant in a Postgres database table located in a dedicated schema that is separate from the schema hosting the historical `purchase_requisitions` data, so that created requisitions never mix with the ingested history.

#### Scenario: Created requisition row exists in the dedicated schema
- **WHEN** the `create_requisition` tool persists a confirmed requisition
- **THEN** the requisition is stored as a row in a table under the dedicated schema and no row is written to the historical `purchase_requisitions` schema

### Requirement: Persisted requisition fields
The system SHALL persist exactly the six fields collected by the skill for each created requisition: `SupplierCode`, `Item`, `Description`, `Quantity`, `Date`, and `Requester`, together with a unique requisition id.

#### Scenario: All six fields stored
- **WHEN** the `create_requisition` tool persists a confirmed requisition
- **THEN** the stored row contains the supplier code, item code, description, quantity, date, requester, and a unique id for the created requisition

### Requirement: Created requisition id returned on success
The system SHALL return the id of the created requisition when persistence succeeds, replacing the previous file-name response.

#### Scenario: Success returns created id
- **WHEN** a requisition is successfully persisted to the environment
- **THEN** the tool response reports the created requisition id

### Requirement: Historical master data unaffected by created requisitions
The system SHALL keep the historical `purchase_requisitions` table in the default schema unchanged by the creation of new requisitions; created requisitions SHALL NOT be embedded, re-ingested, or added to the historical dataset.

#### Scenario: Created requisition not added to history
- **WHEN** the `create_requisition` tool persists a new requisition
- **THEN** the historical `purchase_requisitions` table and its embeddings are unchanged and the new requisition is not returned by retrieval over the historical dataset

#### Scenario: Creation does not trigger ingestion
- **WHEN** a requisition is persisted to the dedicated schema
- **THEN** no ingestion or embedding run is triggered for the historical dataset