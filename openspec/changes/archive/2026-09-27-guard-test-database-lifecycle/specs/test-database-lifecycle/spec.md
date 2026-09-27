## ADDED Requirements

### Requirement: Test database teardown is verified rather than assumed
The system SHALL be guarded by automated tests that fail when the create-and-drop lifecycle or its host resolution stops behaving as this specification requires, so that a regression in teardown is detected by the test suite instead of surfacing later as accumulated orphaned databases on a server.

#### Scenario: A drop that stops removing its database fails a test
- **WHEN** the teardown no longer removes the database it was given
- **THEN** a test asserting the database's absence from the server's database catalog fails, rather than the regression passing silently and leaving a catalog behind

#### Scenario: Dropping an already-absent database is exercised
- **WHEN** teardown runs for a database that is not present
- **THEN** a test drives that path and requires it to complete without error, so the no-op stays a no-op

#### Scenario: A broken host fallback fails a test
- **WHEN** host resolution stops selecting the container-reachable host for a containerised run, or stops honouring an explicitly supplied connection string
- **THEN** a test fails, rather than the error appearing only as integration tests that cannot reach Postgres

#### Scenario: The guard does not depend on the ambient environment
- **WHEN** the host-resolution assertions run
- **THEN** they exercise every combination of an explicitly supplied connection string and container detection directly, without mutating process-global state that other test classes read while running in parallel

### Requirement: An interrupted test run is not cleaned up by the test process
The system SHALL NOT attempt to remove a `prrag_test_*` database from within a test run after that run's process has been terminated, and SHALL NOT add retry, deferred, or recovery logic that attempts to compensate for it, because a terminated process cannot execute its own teardown and the resulting catalog is an operator-cleanup matter rather than a defect the suite can reach.

#### Scenario: A terminated run leaves its database behind
- **WHEN** a test host process is terminated partway through a run
- **THEN** no teardown executes for the database it had created, and the absence of cleanup is recorded as out of scope rather than treated as a lifecycle failure

#### Scenario: The lifecycle requirements are scoped to runs that complete
- **WHEN** this specification's teardown requirements are evaluated
- **THEN** they are read as applying to test classes that complete, including those that fail or run only partially, and not to a process that no longer exists to run them
