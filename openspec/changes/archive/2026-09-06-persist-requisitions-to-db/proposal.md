## Why

Created purchase requisitions currently land as JSON files in a bind-mounted `./requisitions` directory. This couples the API to a writable filesystem mount (with non-root ownership issues), gives no queryability, and keeps created requisitions out of the reach of tooling that can read them back. Persisting them in Postgres removes the filesystem dependency and makes created requisitions first-class, queryable data.

## What Changes

- **BREAKING**: `create_requisition` stops writing JSON files to the requisitions directory and instead inserts the confirmed requisition into a Postgres table.
- The new table lives in a dedicated database schema (e.g. `created`) that isolates created requisitions from the historical `purchase_requisitions` data in the default schema.
- A new EF Core entity + `DbSet` and an EF Core migration create the schema and table; migrations already auto-apply on API startup.
- `IRequisitionWriter` / `RequisitionWriteResult` contract updates: the writer returns a created requisition id (instead of a file name) used in the chat confirmation message.
- The `FileRequisitionWriter`, `Requisitions__Directory` config, and the `./requisitions` bind mount become vestigial and are removed.
- Tests that assert on written JSON files move to asserting on the database.

## Capabilities

### New Capabilities
- `created-requisition-persistence`: stores confirmed purchase requisitions created by the assistant in a dedicated Postgres schema and table, persists the exact six fields collected by the skill, and exposes the created requisition id on success.

### Modified Capabilities
- `skill-framework`: the `create_requisition` tool requirement changes from "write a JSON file under the requisitions directory" to "persist the requisition in the database"; the success response becomes the created requisition id instead of a file name.
- `purchase-requisition-creation-guard`: the guard requirement changes from "does not write any file" to "does not persist anything to the database" for refused/unknown item-supplier combinations; the approval scenario returns the database-persisted requisition id.

## Impact

- `backend/src/PrRag.Infrastructure`: new entity, `DbSet`, schema mapping in `PrRagDbContext`, new `DbRequisitionWriter` replacing `FileRequisitionWriter`, new migration, DI change in `DependencyInjection.cs`.
- `backend/src/PrRag.Application`: `IRequisitionWriter`/`RequisitionWriteResult` update, `ChatService` confirmation message (`ChatService.cs:248-292`), system prompt/hard-coded tool description text.
- `backend/tests/PrRag.Tests`: `FileRequisitionWriterTests`, `SkillFrameworkTests`, `ItemSupplierCombinationTests` updated to assert against Postgres.
- `docker-compose.yml` / devcontainer: remove `./requisitions` bind mount and `Requisitions__Directory` env; entrypoint no longer chowns the requisitions dir.
- Requires a reachable Postgres (already a test prerequisite).