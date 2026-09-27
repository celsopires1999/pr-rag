## Why

The `test-database-lifecycle` spec states four requirements about creating and dropping throwaway test databases, and the implementation satisfies all four — but **no test checks any of them**. Nothing in the suite queries `pg_database`, and the spec is referenced nowhere outside itself. A regression that dropped the `DropDatabaseAsync` call from a test class, or broke the `Host=db` detection that makes teardown work inside the DevContainer, would fail zero tests and leak silently.

That is not hypothetical. 14 orphaned `prrag_test_*` databases had accumulated on the dev Postgres, unnoticed, and the signal that would have explained them does not exist anywhere in the repo. This change adds the missing guard so the requirement is checkable rather than merely asserted.

## What Changes

- Add a `TestDatabaseLifecycleTests` class that exercises `TestDatabase.DropDatabaseAsync` and asserts the database is absent from `pg_database` afterwards, covering the drop itself and the already-absent no-op in one path.
- Add assertions over host resolution that pin all four combinations of `TEST_CONNECTION_STRING` and container detection, so the two host scenarios stop depending on the ambient environment to be meaningful.
- Record the process-death case in the spec as explicitly out of scope, so a later reader does not re-investigate it as an apparent leak.

**Two test-infrastructure edits, no production code changes.** `TestDatabase.cs` is already correct and none of its behaviour changes. It gains `CreateDatabaseAsync`, because a drop cannot be asserted against a catalog that was never created, and a pure `ResolveTemplate` seam, so host resolution can be checked without mutating a process-global environment variable that twelve other classes read concurrently. Both are detailed in design.md; the second exists specifically because the alternative test would be flaky against parallel integration tests. No file under `PrRag.Application`, `PrRag.Infrastructure`, or `PrRag.Api` is touched.

**Not included, deliberately:** a cleanup script for databases orphaned by force-killed test runs. The dev database is disposable, orphaned catalogs cost nothing, and a sweep script is a second thing to maintain against a failure mode that only occurs when someone interrupts a test run.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `test-database-lifecycle`: adds a requirement that the teardown behaviour is verified by a test rather than assumed, and records the force-killed-process case as out of scope so the absence of in-process cleanup is documented rather than rediscovered.

## Impact

**Code:** new file `backend/tests/PrRag.Tests/TestDatabaseLifecycleTests.cs`, plus `CreateDatabaseAsync` and a pure `ResolveTemplate` seam in `backend/tests/PrRag.Tests/TestDatabase.cs`. Both edits to `TestDatabase.cs` are behaviour-preserving. No production projects touched.

**Dependencies:** none new. The suite already requires a reachable Postgres, and the new tests use the same `TEST_CONNECTION_STRING` resolution every other integration test uses.

**Environment:** the new tests create and drop their own `prrag_test_*` databases, so they leave the server as they found it. They are hermetic in the same way the rest of the integration suite is, and do not disturb the 12 existing database-backed classes.
