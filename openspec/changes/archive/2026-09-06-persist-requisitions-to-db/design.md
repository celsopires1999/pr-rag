## Context

Created purchase requisitions are today written to disk by `FileRequisitionWriter`
(`backend/src/PrRag.Infrastructure/Services/FileRequisitionWriter.cs`), which serializes a
`NewPurchaseRequisition` DTO to `requisition-{timestamp}-{guid}.json` under the `Requisitions__Directory`
(`./requisitions`, `/app/requisitions` in the image). The writer is a singleton behind
`IRequisitionWriter` (`backend/src/PrRag.Application/Abstractions/IRequisitionWriter.cs`),
registered in `DependencyInjection.cs:51`, and consumed only by `ChatService.CreateRequisitionAsync`
(`backend/src/PrRag.Application/Services/ChatService.cs:248-292`), which returns `"Requisition created: {result.FileName}."`.

The historical dataset lives in Postgres under the default `public` schema as
`purchase_requisitions` (with embeddings), configured in `PrRagDbContext.OnModelCreating`
(`backend/src/PrRag.Infrastructure/Persistence/PrRagDbContext.cs`). No current `HasSchema` /
`ToTable(name, schema)` mapping exists — both existing tables use the default schema. Nothing reads
the requisitions folder back in production; it exists purely as an output mount.

Goal: persist created requisitions to Postgres in a **new schema** that isolates them from the
historical dataset, and remove the filesystem writer and its mount.

## Goals / Non-Goals

**Goals:**
- `create_requisition` persists confirmed requisitions as rows in a new dedicated Postgres schema.
- The historical `public.purchase_requisitions` data, embeddings, and retrieval are untouched.
- The confirmation returned to the model reports a database id instead of a file name.
- Remove the filesystem writer, `Requisitions__Directory` config, `./requisitions` bind mount, and entrypoint/Dockerfile handling of that directory.

**Non-Goals:**
- No listing/history/UI feature for created requisitions (no read consumer yet — just storage).
- No embedding or vector columns on the new table; created requisitions are not part of RAG retrieval.
- No schema for the historical data (it stays in `public`).
- No change to the item-supplier combination guard logic other than its wording (persist vs. write file).

## Decisions

### 1. Dedicated schema `created` for new requisitions
Created requisitions map to schema **`created`**, table **`created_requisitions`**, keeping
`public.purchase_requisitions` untouched for the history.

**Rationale**: a separate schema is exactly the requested isolation — it cleanly separates "created by
the assistant" from "ingested history" at the database level without touching the historical table's
columns or embeddings.

**Alternatives considered**:
- *Same table, filter flag* — rejected: mixes two datasets, risks polluting retrieval/status counts, and does not satisfy the schema-isolation requirement.
- *New table in `public`* — rejected: weaker isolation and no visual/administrative separation from history.

### 2. Per-table schema mapping (not `HasDefaultSchema`)
`PrRagDbContext.OnModelCreating` maps the new entity with `ToTable("created_requisitions", "created")`
and leaves existing entities on the default schema. The EF Core migration emits
`CREATE SCHEMA IF NOT EXISTS "created"` automatically for the mapped non-default schema.

**Rationale**: explicit and surgical — only the new table changes schema; history stays where it is.

**Alternatives considered**:
- `HasDefaultSchema("created")` on the model — rejected: would silently move *all* new/undecorated tables and require remapping history explicitly.
- Npgsql connection-string `Search Path` — rejected: context-wide, error-prone, and invisible in the model.

### 3. New `CreatedRequisition` entity with a server-less Guid key
Add `CreatedRequisition` in `PrRag.Application/Domain` with:
- `Id` (`Guid`, generated client-side) — returned as the confirmation id without a DB round-trip;
- the six DTO fields persisted as plain columns: `SupplierCode`, `Item`, `Description`,
  `Quantity` (`decimal`), `Date` (`string`, ISO yyyy-MM-dd), `Requester`;
- `CreatedAt` (`DateTimeOffset`, default UTC) for ordering and ops.

No vector/embedding property; a plain entity with no embedding keeps the table outside the RAG pipeline.

### 4. Replace the writer with `DbRequisitionWriter`, keep the `IRequisitionWriter` abstraction
Create `DbRequisitionWriter : IRequisitionWriter` in Infrastructure that inserts a
`CreatedRequisition` via `PrRagDbContext`. It keeps the existing `requisition.Validate()` check and
moves the id generation into the entity. DI registration at `DependencyInjection.cs:51` changes from a
singleton `FileRequisitionWriter` to a **scoped** `DbRequisitionWriter` (it needs the scoped
`PrRagDbContext`).

**Rationale**: ChatService depends only on the abstraction, so the swap is the minimal wiring change;
scoped lifetime matches the DbContext. Validation stays in the Application DTO, preserving the
"refuse on missing/invalid fields" behavior.

### 5. Result contract changes from `FileName` to `RequisitionId`
`RequisitionWriteResult` becomes `record RequisitionWriteResult(bool Success, string? RequisitionId,
string? Error)` with `Ok(string requisitionId)` / `Fail(string error)`. `ChatService.CreateRequisitionAsync`
returns `$"Requisition created: {result.RequisitionId}."`. The hard-coded tool description in
ChatService's `RegisterFunction` call (currently "Persists a new purchase requisition to disk as a JSON
file") and the system-prompt text update to describe database persistence.

### 6. Remove the filesystem wiring
- `RequisitionsSettings` config class and its `Configure<RequisitionsSettings>` registration removed.
- `Requisitions__Directory` env in `docker-compose.yml` (lines 44, 100) and the `./requisitions` bind
  mount (line 51) removed.
- `backend/Dockerfile` no longer `mkdir`s/chowns `/app/requisitions`; `backend/entrypoint.sh` only
  chowns `/app/reports`.
- Devcontainer/dotfiles references to a requisitions directory removed if present.

**Rationale**: with persistence in Postgres the mount exists only to work around filesystem-write
problems (root-owned dirs causing 500s) that no longer apply, so it is deleted rather than retained.

## Risks / Trade-offs

- [New table/schema on upgrade] → The migration is additive; `DbInitializer.ApplyMigrationsAsync` runs it
  at startup. Rollback = `Down()` dropping the table and schema.
- [Scoped vs singleton DI change] → Only ChatService consumes `IRequisitionWriter`; verified no other
  registrant relies on singleness. Tests must not hold the writer across scopes.
- [Guid key vs DB-generated identity] → Client-side Guid avoids a second query and returns the id
  immediately; negligible ordering concerns are covered by `CreatedAt`.
- [Schema existence for test databases] → Migrations (including the schema) apply in tests the same as
  prod via `DbInitializer`; test helpers that call `MigrateAsync` cover it.
- [Residual references to "file"/"directory"] → Wording in skill docs/tool descriptions may still say
  "write JSON file"; updated in ChatService prompt and confirmed by grep before done.

## Migration Plan

1. Add entity + mapping + migration (`dotnet ef migrations add AddCreatedRequisitions`).
2. Swap writer and result contract; update ChatService text.
3. Update tests (fail the JSON-file assertions) and DB assertions.
4. Strip filesystem wiring (config, compose, Dockerfile, entrypoint).
5. Verify: `dotnet build`, run tests with a reachable Postgres.

Rollback: revert code; `Down()` migration drops `created.created_requisitions` and the schema; the
filesystem writer path can be restored from git history if ever needed.

## Open Questions

- **Schema name**: `created` is proposed; `requisitions_created` / `app` are acceptable alternatives if
  preferred. (Decision defaults to `created`.)