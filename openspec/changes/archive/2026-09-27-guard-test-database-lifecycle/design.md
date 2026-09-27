## Context

`test-database-lifecycle` states four requirements. `TestDatabase.cs` satisfies all four: `DropDatabaseAsync` issues `DROP DATABASE IF EXISTS ... WITH (FORCE)`, and `ConnectionStringTemplate` resolves the host through `TEST_CONNECTION_STRING`, falling back to `Host=db` in a DevContainer and `Host=localhost` otherwise. Twelve test classes create a `prrag_test_*` database and twelve drop them, one to one.

The gap is verification, not behaviour. Nothing in the suite queries `pg_database`, no test references the spec, and the spec is cited nowhere outside itself. Meanwhile 14 orphaned databases had accumulated on the dev server, which is exactly the failure this requirement exists to prevent and exactly the kind that a missing guard lets pass unnoticed.

Two properties of the existing code make a naive guard either impossible or flaky, and both shape the decisions below.

## Goals / Non-Goals

**Goals:**
- Make each of the four existing requirements fail a test when it stops holding.
- Add no production (`PrRag.Application` / `PrRag.Infrastructure` / `PrRag.Api`) code changes.
- Leave the suite's hermeticity unchanged: the new tests create and drop only their own throwaways.
- Record the force-killed-process case in the spec as out of scope, so its absence is documented rather than rediscovered.

**Non-Goals:**
- Changing teardown behaviour. `TestDatabase.cs` is correct today; every edit to it is a testability change, not a fix.
- Cleaning up databases orphaned by interrupted runs. The dev database is disposable, the catalogs cost nothing, and a sweep script is a second thing to maintain against a failure mode that only occurs when someone interrupts a run.
- Reducing the cost of the existing 12 database-backed classes.

## Decisions

### 1. Extract a pure seam for host resolution rather than mutating the environment in a test

`ConnectionStringTemplate` reads `TEST_CONNECTION_STRING` and inspects the filesystem, and exposes no seam. The obvious test therefore sets the environment variable and asserts on the result — but xUnit runs test classes in parallel, and twelve integration classes read that same variable to build their connection strings. A test that mutates it can interleave with a class that is mid-`InitializeAsync` and hand it a connection string pointing at the wrong database. That failure is intermittent, timing-dependent, and would surface as unrelated integration tests failing for no visible reason.

The alternative is to assert only on the ambient value — "in a container, this contains `Host=db`". That is worse than useless: it tests the environment rather than the logic, passes vacuously whenever the fallback happens to coincide with reality, and cannot reach the `TEST_CONNECTION_STRING` branch at all.

So host resolution is split into a pure function that takes both inputs, with the property delegating to it. `ResolveTemplate(testConnectionString, isInsideDevContainer)` is then checked for all four combinations with no process-global state touched and no parallelism hazard. Four lines, behaviour-preserving, and the override branch finally becomes reachable by a test.

### 2. Add a create helper, because a drop cannot be tested against nothing

To assert the drop removed something, something must exist first. The twelve existing classes get their database created implicitly, as a side effect of EF's `MigrateAsync` — which is the wrong mechanism here, since this test is about teardown and should not pay for migrations or a seeded schema to obtain a catalog to drop.

`TestDatabase.CreateDatabaseAsync` is added beside `DropDatabaseAsync`, issuing `CREATE DATABASE` over the same resolved template. The pair becomes explicit and, more usefully, symmetric: a helper that can only drop is a helper whose counterpart nobody can verify.

### 3. The guard is a plain class, not an `IAsyncLifetime` integration test

The default shape for anything in this project touching Postgres is a class with its own `prrag_test_*` database, a `ServiceProvider`, migrations, and a seed array. Applying it here would make a one-second assertion cost the same thirty seconds as a real integration test, and would add a thirteenth database to the lifecycle that this change exists to keep honest.

This test needs a *server*, not a *schema*. It therefore talks to the maintenance database directly and owns nothing but the two catalogs it creates and drops itself. The asymmetry is deliberate and worth recording, because "make it look like the other tests" is the reflex this decision is rejecting.

### 4. Assert through `pg_database`, and prove the no-op by dropping twice

Presence is queried on `pg_database` over the resolved template — the same host the drop used, so a wrong-host regression fails the assertion rather than passing against a different server. Two assertions, because they cover two different requirements: after one drop the catalog is absent, and a second `DropDatabaseAsync` for the same name returns without throwing, which is what `IF EXISTS` is for.

The second assertion is the cheaper half of the pair and the one more likely to be dropped in a refactor, since a redundant-looking drop looks like dead code. It is the only thing standing between a typo in the `IF EXISTS` clause and a teardown that throws on every already-cleaned database.

### 5. Guard both edits by making them fail loudly if reverted

The seam in decision 1 and the create helper in decision 2 are the only ways this change can be silently undone, since neither is load-bearing for any other test. Each is pinned: reverting `ResolveTemplate` to read the environment inline fails the four-combination assertion, and removing `CreateDatabaseAsync` fails compilation of the test that needs it. That is checked by running the suite with the edit reverted, not asserted as a claim.

## Risks / Trade-offs

- **Process-global environment mutation races parallel integration classes** → eliminated by decision 1; the new tests never call `SetEnvironmentVariable`.
- **The guard test itself leaks if it fails between create and drop, becoming the defect it exists to prevent** → the create and drop are adjacent with no assertion between them, and `DropDatabaseAsync` is `IF EXISTS`, so a re-run is safe and cannot fail on the leftover.
- **`ResolveTemplate` is a behaviour-preserving refactor of shared test infrastructure used by twelve classes** → the change is a pure extraction with the original expression moved verbatim, and the full suite is the regression check.
- **The new tests require a reachable Postgres, so they cannot run in an environment where the rest of the suite cannot either** → no new failure mode; it fails wherever the other twelve already do.
- **A test about teardown that only passes when teardown works cannot detect a server that is down** → accepted. It is a guard against regression, not a health check; the suite's existing reachability assumptions are unchanged.

## Open Questions

None blocking. The one judgement call worth surfacing to a reviewer: the spec gains a requirement asserting the lifecycle is *verified*, which is a requirement about the test suite rather than about the system under test. If that is considered out of character for this spec, the alternative is to record the guard in `AGENTS.md` alone and leave the spec unchanged — at the cost of the one place a future reader is guaranteed to look.
