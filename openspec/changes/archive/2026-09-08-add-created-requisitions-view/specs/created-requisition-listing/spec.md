# Created Requisition Listing

## Purpose

Expose created requisitions over a REST endpoint with server-side pagination, column sorting, per-column filtering, and optional session scoping, so the web front-end can browse them without loading the entire table into the browser.

## ADDED Requirements

### Requirement: List created requisitions
The system SHALL provide a REST endpoint that returns created requisitions, paginated server-side, ordered by `CreatedAt` descending by default.

#### Scenario: Retrieve first page
- **WHEN** a client requests the created-requisitions endpoint with no parameters
- **THEN** the endpoint returns the first page of requisitions ordered by `CreatedAt` descending, together with the total count

#### Scenario: Request a specific page
- **WHEN** a client requests a page number beyond the first
- **THEN** the endpoint returns that page's requisitions respecting the page size

### Requirement: Sort by any supported column
The system SHALL let the client order results by any exposed column, in ascending or descending order.

#### Scenario: Sort by a selected column
- **WHEN** a client requests results sorted by a supported column in a given direction
- **THEN** the endpoint returns results ordered by that column accordingly

#### Scenario: Skip sorting on unsupported columns
- **WHEN** a client requests sorting by an unsupported column
- **THEN** the endpoint ignores the sort request and falls back to the default ordering

### Requirement: Filter per column
The system SHALL support filtering results by supplier code, item, description, requester, quantity, and date, matching the corresponding column values.

#### Scenario: Filter by supplier code
- **WHEN** a client requests results filtered by a supplier code value
- **THEN** the endpoint returns only requisitions whose supplier code matches the filter

#### Scenario: Filter by item
- **WHEN** a client requests results filtered by an item value
- **THEN** the endpoint returns only requisitions whose item matches the filter

#### Scenario: Filter by description
- **WHEN** a client requests results filtered by a description value
- **THEN** the endpoint returns only requisitions whose description matches the filter

#### Scenario: Filter by requester
- **WHEN** a client requests results filtered by a requester value
- **THEN** the endpoint returns only requisitions whose requester matches the filter

#### Scenario: Filter by quantity
- **WHEN** a client requests results filtered by a quantity value
- **THEN** the endpoint returns only requisitions whose quantity matches the filter

#### Scenario: Filter by date
- **WHEN** a client requests results filtered by a date value
- **THEN** the endpoint returns only requisitions whose date matches the filter

### Requirement: Scope results by session
The system SHALL support scoping results to the requisitions created during a single chat session, identified by session id.

#### Scenario: Filter by session id
- **WHEN** a client requests results scoped to a session id
- **THEN** the endpoint returns only requisitions created in that session

#### Scenario: Unscoped results include all sessions
- **WHEN** a client requests results without a session id
- **THEN** the endpoint returns requisitions from every session, including requisitions with no recorded session

### Requirement: Read-only data contract
The listing endpoint SHALL expose read-only requisition data; it SHALL NOT create, update, or delete requisitions.

#### Scenario: No writes through listing endpoint
- **WHEN** a client uses the listing endpoint
- **THEN** no requisition is created, modified, or deleted as a side effect