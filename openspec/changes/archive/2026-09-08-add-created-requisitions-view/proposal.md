## Why

Users create purchase requisitions through the chat agent but have no way to review them afterwards: the only visibility is the transient agent reply. The system needs a dedicated page in the web front-end where users can browse, filter, and inspect the requisitions they created, so the agent's output becomes auditable and reusable.

## What Changes

- Adds a `SessionId` column to the `created.created_requisitions` table (new migration) and captures the active chat session id in the `create_requisition` tool so requisitions can be attributed to a session.
- Adds a server-side listing endpoint `GET /api/created-requisitions` supporting pagination, column sorting, per-column filtering, and an optional session filter (`current` | none). Also allows a client-side "current session" shortcut for the active chat session.
- Adds a new front-end route `/requisitions` with a shadcn/ui table page: sortable/filterable columns, server-side pagination, a session selector with exactly two fixed options (`All sessions` / `Current session`), a manual refresh button, and a read-only detail dialog for each requisition.
- Adds shadcn/ui `table` component and the supporting UI primitives to the front-end; all new UI copy is in English.
- Backfills `SessionId` for already-persisted requisitions with `NULL` (they are not attributed to any session).
- **BREAKING**: the persisted-requisition field contract changes from "exactly six fields" to include the session id; existing rows keep a null session id.

## Capabilities

### New Capabilities
- `created-requisition-listing`: server-side retrieval of created requisitions with pagination, sort, per-column filters, and session scoping, exposed over a REST endpoint.
- `requisitions-page`: web front-end page for browsing and inspecting created requisitions (table, filters, pagination, session toggle, detail dialog, refresh).

### Modified Capabilities
- `created-requisition-persistence`: persisted requisitions now also store the chat session id that created them, so they can be attributed and later filtered by session.
- `web-frontend`: sidebar navigation grows a third destination (Requisitions) alongside Chat and System Status.

## Impact

- **Backend**: `CreatedRequisition` domain entity, `PrRagDbContext` mapping, EF Core migration, `DbRequisitionWriter`, `PurchaseRequisitionTools`/`NewPurchaseRequisition` DTO, new repository/query surface and a new endpoint in `PrRag.Api`; `IRequisitionWriter` signature.
- **Frontend**: new route entry in `App.tsx`, new page component, shadcn `table` component added to `src/components/ui`, new API client functions in `api.ts`, new types in `types.ts`, sidebar navigation update.
- **Data**: `created.created_requisitions` schema change (additive column).
- **Tests**: integration coverage for the listing endpoint, session attribution, and filtering/sorting/pagination.