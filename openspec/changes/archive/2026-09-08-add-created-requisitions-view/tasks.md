## 1. Domain & Persistence (session attribution)

- [x] 1.1 Add nullable `SessionId` property to `CreatedRequisition` (`backend/src/PrRag.Application/Domain/CreatedRequisition.cs`)
- [x] 1.2 Add nullable `SessionId` to `NewPurchaseRequisition` DTO (`backend/src/PrRag.Application/DTOs/NewPurchaseRequisition.cs`)
- [x] 1.3 Update `IRequisitionWriter.WriteAsync` to accept an optional `sessionId` argument and map it onto the created entity (`backend/src/PrRag.Application/Abstractions/IRequisitionWriter.cs`, `backend/src/PrRag.Infrastructure/Services/DbRequisitionWriter.cs`)
- [x] 1.4 Thread `_turnContext.Session.Id` into `CreateRequisitionAsync`'s writer call in `PurchaseRequisitionTools` (`backend/src/PrRag.Application/Services/Agents/PurchaseRequisitionTools.cs`)
- [x] 1.5 Add EF Core migration `AddCreatedRequisitionSession` adding nullable `session_id` to `created.created_requisitions` (`dotnet ef migrations add` from `backend/`)

## 2. Listing query surface

- [x] 2.1 Add `ICreatedRequisitionQuery` abstraction to Application (`backend/src/PrRag.Application/Abstractions/`) with filters/sort/pagination input and paged result DTOs (`CreatedRequisitionDto`, page envelope)
- [x] 2.2 Implement `CreatedRequisitionQuery` in Infrastructure using `PrRagDbContext`: case-insensitive contains filters, quantity/date/created-at ranges, session-id scope, whitelisted sort with `createdAt desc` default, total count
- [x] 2.3 Register `ICreatedRequisitionQuery` in AddApplication (or AddInfrastructure) DI wiring (`backend/src/PrRag.Infrastructure/DependencyInjection.cs`)

## 3. API endpoint

- [x] 3.1 Add `GET /api/created-requisitions` handler in `backend/src/PrRag.Api/Program.cs` parsing `page`, `pageSize`, `sortBy`, `sortDir`, text/ranged filters, and optional `sessionId`
- [x] 3.2 Validate/clamp params (page ≥ 1, pageSize 1–100, unknown sortBy falls back to default) and return the paged DTO envelope

## 4. Backend tests

- [x] 4.1 Integration test: listing endpoint returns all requisitions ordered by `CreatedAt` descending with total count
- [x] 4.2 Integration test: pagination (page size + page number) returns the correct slice
- [x] 4.3 Integration test: sorting by a supported column both directions
- [x] 4.4 Integration test: per-column filters (supplier code, item, description, requester, quantity range, date range, created-at range)
- [x] 4.5 Integration test: `sessionId` scoping (rows in session, rows in other sessions, rows with null session) — null-session rows only appear unscoped
- [x] 4.6 Integration test: `create_requisition` captures the session id; verify via listing (`DbRequisitionWriterTests` or agentic tests)
- [x] 4.7 Integration test: writer accepts a null session id (legacy row tolerated)

## 5. Front-end data layer

- [x] 5.1 Add `CreatedRequisition`, `CreatedRequisitionPage`, and query-params types to `frontend/src/types.ts`
- [x] 5.2 Add `listCreatedRequisitions(params)` client function building the query string in `frontend/src/api.ts`

## 6. Front-end UI

- [x] 6.1 Add shadcn `table` component via the shadcn CLI
- [x] 6.2 Add `/requisitions` route to `frontend/src/App.tsx` and a sidebar link in `frontend/src/components/AppSidebar.tsx` (English label, e.g. "Requisitions")
- [x] 6.3 Create `frontend/src/pages/RequisitionsPage.tsx`: fetch-on-mount (refetch on navigation), manual refresh button, abort on change to avoid races
- [x] 6.4 Render the table with columns supplier code, item, description, quantity, date, requester, creation time; default sort `createdAt` desc
- [x] 6.5 Implement sortable headers (toggle asc/desc) with per-column filter controls (inputs + range inputs for quantity/date/created-at)
- [x] 6.6 Implement server-side pagination (prev/next + page indicator), resetting to page 1 when filters or session scope change
- [x] 6.7 Add the fixed two-option session selector ("All sessions" / "Current session") resolving to `localStorage.prrag.session_id`, falling back to all sessions when absent
- [x] 6.8 Add loading/empty/error states (skeletons on load, empty-state message when no requisitions)
- [x] 6.9 Add read-only detail `Dialog` on row click showing all fields including full description, with no actions
- [x] 6.10 Confirm all UI copy on the page is English

## 7. Front-end verification

- [x] 7.1 Run `npm run build` and `npm run lint` in `frontend/` successfully

## 8. Full verification

- [x] 8.1 Run `dotnet build backend/PrRag.sln` successfully
- [x] 8.2 Run `dotnet test backend/tests/PrRag.Tests` (with `TEST_CONNECTION_STRING`) successfully