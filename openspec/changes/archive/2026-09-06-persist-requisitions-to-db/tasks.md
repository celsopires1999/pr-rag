## 1. Data Model

- [x] 1.1 Add `CreatedRequisition` domain entity in `backend/src/PrRag.Application/Domain` with `Id` (Guid, client-side), `SupplierCode`, `Item`, `Description`, `Quantity` (decimal), `Date` (string), `Requester`, and `CreatedAt` (DateTimeOffset)
- [x] 1.2 Map the entity in `PrRagDbContext.OnModelCreating` with `ToTable("created_requisitions", "created")` and column mappings consistent with the existing style
- [x] 1.3 Add EF Core migration `AddCreatedRequisitions` (from `backend/`, `dotnet ef migrations add`) verifying it emits `CREATE SCHEMA IF NOT EXISTS "created"` and `CREATE TABLE "created"."created_requisitions"`, and updating the model snapshot

## 2. Persistence

- [x] 2.1 Update `RequisitionWriteResult` in `backend/src/PrRag.Application/Abstractions/IRequisitionWriter.cs` from `FileName` to `RequisitionId` (`Ok(string requisitionId)` / `Fail(string error)`)
- [x] 2.2 Create `DbRequisitionWriter : IRequisitionWriter` in `PrRag.Infrastructure/Services` that validates via `requisition.Validate()`, builds a `CreatedRequisition` with a new Guid, inserts it through `PrRagDbContext`, and returns `Ok(id)` or `Fail(error)`
- [x] 2.3 Remove `FileRequisitionWriter.cs` and register `IRequisitionWriter` as a scoped `DbRequisitionWriter` in `DependencyInjection.cs` (replacing the singleton registration)
- [x] 2.4 Remove the `RequisitionsSettings` config class and its `Configure<RequisitionsSettings>` registration in `DependencyInjection.cs`

## 3. Chat Service

- [x] 3.1 Update `ChatService.CreateRequisitionAsync` to report `Requisition created: {result.RequisitionId}.`
- [x] 3.2 Update the `create_requisition` tool description in `ChatService.RegisterFunction` (remove "to disk as a JSON file") to describe database persistence
- [x] 3.3 Update the system-prompt text for `create_requisition` to say the requisition is persisted to the database

## 4. Infra / Compose Cleanup

- [x] 4.1 Remove `Requisitions__Directory` env entries and the `./requisitions` bind mount from `docker-compose.yml`
- [x] 4.2 Update `backend/Dockerfile` to stop creating/chowning `/app/requisitions`
- [x] 4.3 Update `backend/entrypoint.sh` to only `mkdir`/`chown` `/app/reports`
- [x] 4.4 Grep the repo (excluding `openspec/` and `node_modules`) for remaining "requisitions directory", "JSON file", or "Requisitions__Directory" references tied to file persistence and clean up any leftovers in docs/AGENTS.md if present

## 5. Tests

- [x] 5.1 Add `DbRequisitionWriterTests` covering success insert (row exists in `created.created_requisitions` with all six fields), missing/invalid fields returning `Fail` with no insert, and duplicate-safe Guid ids
- [x] 5.2 Update `SkillFrameworkTests` requisition-persistence assertions to verify the row in `created.created_requisitions` and the created id in the response instead of a JSON file
- [x] 5.3 Update `ItemSupplierCombinationTests` file-read guarantee to assert nothing is persisted in the `created` schema for the refused combination
- [x] 5.4 Add an isolation test asserting a created requisition does not appear in `public.purchase_requisitions` and `CreateRequisitionAsync` does not trigger ingestion/embedding
- [x] 5.5 Remove/replace `FileRequisitionWriterTests.cs` with the new DB writer tests

## 6. Verification

- [x] 6.1 Run `dotnet build backend/PrRag.sln` and fix any compile errors
- [x] 6.2 Run the integration tests against a reachable Postgres (`dotnet test backend/tests/PrRag.Tests` with `TEST_CONNECTION_STRING`) and confirm all pass
- [x] 6.3 Confirm no `Requisitions__Directory` or requisitions-folder references remain in runtime config/compose after grep