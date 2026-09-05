## Context

Integration tests in `tests/PrRag.Tests` run against a real Postgres. Each test class implements `IAsyncLifetime`, builds a connection string from `TestDatabase.ConnectionStringTemplate` (which honors `TEST_CONNECTION_STRING`, DevContainer `Host=db`, or `Host=localhost`) plus a `Database=prrag_test_<guid>` suffix, and disposes its `ServiceProvider` in `DisposeAsync` — but the created database is never removed. As a result the server has accumulated 178 orphaned `prrag_test_*` databases.

Affected classes: `IngestionDiffTests`, `AgenticRetrievalTests`, `RagObservabilityReportTests` (the only `IAsyncLifetime` integration test classes).

## Goals / Non-Goals

**Goals:**
- Drop the per-class test database on teardown so repeated runs no longer accumulate databases.
- Keep host detection identical to DB creation (same `ConnectionStringTemplate` logic).
- Preserve isolation: each class drops exactly the database it created.

**Non-Goals:**
- Cleanup the ~178 already-orphaned databases (one-time manual action, documented in Migration Plan).
- Centralized collection running, test-sharding, or parallel-class orchestration.
- Changes to how test databases are created or migrated.

## Decisions

**D1. Add `TestDatabase.DropDatabaseAsync(string dbName, CancellationToken)` and call it from each class's `DisposeAsync`.**
The helper connects with `ConnectionStringTemplate` (no `Database=` component, so Npgsql connects to the role's default `prrag` database, which serves as a maintenance DB) and executes `DROP DATABASE IF EXISTS "<dbName>" WITH (FORCE)`. The call goes after `provider.DisposeAsync()` so EF resources are released first.
*Alternatives considered:* `NpgsqlConnection.ClearPool` per connection string — more moving parts than `WITH (FORCE)`, which terminates the pooled idle connections (idle connections are all that remain after provider disposal); a dedicated test-collection fixture (`ICollectionFixture`) — over-engineering for three call sites.

**D2. Use `DROP DATABASE IF EXISTS ... WITH (FORCE)`.**
PG13+ (project runs `pgvector/pgvector:pg18`). Npgsql keeps pooled connections to the just-used database open even after the provider is disposed; without `FORCE` the drop fails with "database ... is being accessed by other users". `FORCE` terminates those connections and drops the database atomically.
*Alternative considered:* `TerminateASync`/`pg_terminate_backend` before `DROP DATABASE` — two statements and more error surface; `FORCE` is a single command.

**D3. Quote the database identifier defensively.**
Database names are generated (`prrag_test_` + GUID hex) and contain no risky characters, but the command still quotes the identifier and doubles embedded quotes to avoid any injection surface if the name ever becomes user-influenced.

**D4. Let teardown exceptions surface.**
A failed drop (e.g., insufficient privileges) is reported by xUnit as a teardown error rather than silently swallowing it, so regressions are visible. The `IF EXISTS` guard makes already-missing databases a no-op (spec requirement).

**D5. Database creation remains unchanged.**
Only the teardown lifecycle is added; tests keep using `Database=prrag_test_<guid>` on the same host template.

## Risks / Trade-offs

- [Drop targets a DB still in use by an active parallel test] → Each class owns a unique GUID-named database and drops only that exact name; no wildcards, so unrelated runs are untouched.
- [`WITH (FORCE)` requires PG13+] → Project already uses PG18; met.
- [Drop fails on permission/host error] → Surfaces at teardown (D4); the role `prrag` owns the databases it creates, so it can drop them.
- [Attaching teardown to the class means a crashed process still leaves orphans] → Acceptable; per-run lifecycle still removes databases in the normal/aborted-but-recovered cases, which is the goal.

## Migration Plan

Deploy is a code change to the test project only; rebuild and run the suite. One-time manual cleanup of the existing orphans (out of scope for this change):
```sql
SELECT 'DROP DATABASE "' || datname || '";'
FROM pg_database
WHERE datname LIKE 'prrag_test_%';
```
(review the generated statements, then execute) — or run `DROP DATABASE ... WITH (FORCE)` per orphan.

## Open Questions

None.