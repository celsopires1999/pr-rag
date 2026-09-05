## Why

Each integration test class creates its own Postgres database (`prrag_test_<guid>`) but never drops it after the run. The Postgres instance currently holds **178 orphaned test databases** that accumulate on every test execution, wasting disk and cluttering the server.

## What Changes

- Add a drop-database helper to `TestDatabase` that connects through the same reachable-host template (`TEST_CONNECTION_STRING`, DevContainer `db`, or `localhost`) and issues `DROP DATABASE ... WITH (FORCE)`.
- Each test class (`IngestionDiffTests`, `AgenticRetrievalTests`, `RagObservabilityReportTests`) calls the helper in `DisposeAsync` after disposing its `ServiceProvider`, so the database it created is removed when the class finishes.
- No changes to how databases are created or which host is used — only the teardown lifecycle.

## Capabilities

### New Capabilities
- `test-database-lifecycle`: requirement that integration-test runs create a dedicated throwaway Postgres database and destroy it when the test class finishes.

### Modified Capabilities
<!-- None. No existing product/spec capability changes. -->

## Impact

- **Code**: `tests/PrRag.Tests/TestDatabase.cs` gains a `DropDatabaseAsync` helper; the three integration test classes' `DisposeAsync` call it.
- **Tests**: rebuilding the suite still passes; databases no longer accumulate.
- **Infrastructure**: `DROP DATABASE IF EXISTS ... WITH (FORCE)` requires Postgres 13+ (project uses `pgvector/pgvector:pg18`). Requires connect/terminate permissions for the `prrag` role, already used by the tests.
- **Existing orphans**: the ~178 already-created databases are out of scope for automated cleanup in this change; they can be removed once with a manual SQL statement.