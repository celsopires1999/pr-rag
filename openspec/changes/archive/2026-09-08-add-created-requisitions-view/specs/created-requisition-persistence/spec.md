# Created Requisition Persistence

## MODIFIED Requirements

### Requirement: Persisted requisition fields
The system SHALL persist the six fields collected by the skill for each created requisition — `SupplierCode`, `Item`, `Description`, `Quantity`, `Date`, and `Requester` — together with a unique requisition id and the chat session id that created the requisition.

#### Scenario: All six fields stored
- **WHEN** the `create_requisition` tool persists a confirmed requisition
- **THEN** the stored row contains the supplier code, item code, description, quantity, date, requester, and a unique id for the created requisition

#### Scenario: Session id stored with the requisition
- **WHEN** the `create_requisition` tool persists a confirmed requisition during an active chat session
- **THEN** the stored row records the session id in which the requisition was created

## ADDED Requirements

### Requirement: Existing requisitions without a session id
The system SHALL tolerate created requisitions that have no recorded session id and SHALL NOT require one to be present.

#### Scenario: Null session id accepted
- **WHEN** a requisition exists without a recorded session id
- **THEN** the requisition remains readable and is included in unscoped queries over created requisitions