## ADDED Requirements

### Requirement: Test database destroyed after run
The system SHALL drop the dedicated Postgres database that an integration-test class creates once that test class completes, so test runs leave no orphaned databases behind.

#### Scenario: Database dropped when test class finishes
- **WHEN** an integration test class completes (including failed or partially-run classes) and has created a `prrag_test_*` database
- **THEN** the database is removed from the Postgres server

#### Scenario: Drop uses the same reachable host
- **WHEN** the teardown cleanup runs outside a DevContainer
- **THEN** it connects through `TEST_CONNECTION_STRING` when set, otherwise `Host=localhost`, matching the connection used to create the database

#### Scenario: Drop works inside the DevContainer
- **WHEN** the teardown cleanup runs inside the DevContainer
- **THEN** it connects through `Host=db`, matching the connection used to create the database

#### Scenario: Non-existent database is a no-op
- **WHEN** the teardown cannot find the database it intends to drop (e.g., already removed externally)
- **THEN** the cleanup completes without error