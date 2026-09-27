## 1. Testability seams in TestDatabase

- [x] 1.1 Extract `ResolveTemplate(string? testConnectionString, bool isInsideDevContainer)` in `TestDatabase.cs`, moving the existing expression verbatim, and have `ConnectionStringTemplate` delegate to it with the environment variable and the container check as arguments. No behaviour change; the point is that host resolution stops reading process-global state at the point of decision.
- [x] 1.2 Add `CreateDatabaseAsync(string dbName, CancellationToken)` beside `DropDatabaseAsync`, issuing `CREATE DATABASE` over the same resolved template and quoting the identifier the way the drop already does. Needed because a drop cannot be asserted against a catalog that was never created, and the existing classes only get theirs as a side effect of EF's `MigrateAsync`.
- [x] 1.3 Confirm the extraction is behaviour-preserving by running the full suite, since twelve existing classes resolve their connection string through the edited property.

## 2. The guard

- [x] 2.1 Create `backend/tests/PrRag.Tests/TestDatabaseLifecycleTests.cs` as a plain class, not an `IAsyncLifetime` class with its own migrated database. It needs a server to create and drop a catalog on, not a schema, and must not add a thirteenth database to the lifecycle it guards.
- [x] 2.2 Cover the four host-resolution combinations through `ResolveTemplate` directly: connection string supplied and container detected, each alone, and neither. Assert the supplied value wins outright and each fallback selects the expected host.
- [x] 2.3 Assert the drop removes its database, by creating one, dropping it through `DropDatabaseAsync`, and asserting absence from `pg_database` over the same resolved template.
- [x] 2.4 Assert dropping an already-absent database completes without error, so `IF EXISTS` stays exercised rather than looking like redundant code.
- [x] 2.5 Confirm the new file never calls `SetEnvironmentVariable`. The suite runs classes in parallel and twelve of them read `TEST_CONNECTION_STRING` to build connection strings, so mutating it here would make them fail intermittently for no visible reason.

## 3. Verification

- [x] 3.1 Run the full suite and confirm it passes.
- [x] 3.2 Confirm a completed run leaves zero `prrag_test_*` databases on the server, which is the property the existing spec requires and the reason 14 orphans went unnoticed before.
- [x] 3.3 Revert 1.1 and confirm 2.2 fails. This is the check the design commits to, so it gets run rather than claimed: the seam is load-bearing for no other test and would otherwise be removable in silence.
- [x] 3.4 Revert the `IF EXISTS` clause and confirm 2.4 fails.
- [x] 3.5 Run the suite more than once and confirm no new test is order-dependent or flaky, since the whole reason for 1.1 was to avoid a parallelism hazard.

## 4. Specs

- [x] 4.1 Sync this change's delta to `openspec/specs/test-database-lifecycle/spec.md`.
- [x] 4.2 Verify the sync mechanically: confirm the main spec contains the new requirement headings. `openspec validate --all` passing is not sufficient evidence of a sync, because validation does not require the delta to be merged and reports a change identically whether or not it has been applied.
- [x] 4.3 Confirm `openspec validate --all` passes after 4.2, and archive the change.
