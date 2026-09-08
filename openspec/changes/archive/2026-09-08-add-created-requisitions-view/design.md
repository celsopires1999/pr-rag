# Add Created Requisitions View — Design

## Context

Created purchase requisitions are persisted to the dedicated `created` schema (`created_requisitions`) by the chat agent's `create_requisition` tool. There is currently no way to list or review them — no read API, no UI. The front-end (React 19 + Vite + shadcn/ui) has two routes (`/`, `/status`), no table component, and talks to the API through thin client functions in `api.ts`. The session id lives in the browser's `localStorage` under `prrag.session_id`, and the backend already tracks it inside the chat session (`AgentSession`), which the tools can reach via `AgentTurnContext.Session`.

This change adds session attribution at creation time, a read-only server-side listing endpoint, and a dedicated `/requisitions` page with a sortable/filterable/paginated table and a read-only detail dialog.

## Goals / Non-Goals

**Goals:**
- Record which chat session created each requisition (`SessionId`, nullable).
- Provide `GET /api/created-requisitions` with server-side pagination, sorting, per-column filters, and an optional `sessionId` scope.
- Add a `/requisitions` front-end page: columns for supplier code, item, description, quantity, date, requester, creation time; sortable headers with per-column filters; a fixed two-option session selector ("All sessions" / "Current session"); manual refresh; read-only detail dialog.
- All UI copy in English.

**Non-Goals:**
- No create/edit/delete actions from the UI or the listing endpoint (creation remains agent-mediated only).
- No embedding or ingestion of created requisitions.
- No arbitrary session list: session filtering is only the two fixed options ("All" / "Current").
- No URL-encoded filter state; filters/sort/pagination live in component state and reset on navigation.

## Decisions

### 1. Session attribution: nullable `SessionId` column on `created_requisitions`
`CreatedRequisition` gains `string? SessionId`. A new EF migration adds nullable `session_id`. `DbRequisitionWriter.WriteAsync` gains a `sessionId` argument so `IRequisitionWriter.WriteAsync(NewPurchaseRequisition, string? sessionId, ct)` — the tool passes `_turnContext.Session.Id`. Existing rows stay `NULL` and are never attributed.
- **Alternative considered:** a separate join table `session_requisitions`. Rejected — overkill for a single nullable column, and the listing endpoint only ever filters by session id.
- **Alternative considered:** deriving session from the chat message at read time. Rejected — creation happens inside the agent run; the session id must be captured at write time.

### 2. Query surface: new `ICreatedRequisitionQuery` abstraction
A new Application abstraction `ICreatedRequisitionQuery.QueryAsync(filters, page, pageSize, sort) -> CreatedRequisitionPage` implemented in Infrastructure over `PrRagDbContext`. Keeps Api → Application → Infrastructure dependency direction and avoids growing the reference-data repository with created-data behavior.
- **Alternative considered:** adding methods to `IPurchaseRequisitionRepository`. Rejected — that repository is scoped to the historical/searchable dataset; mixing the two tables muddies intent.

### 3. Endpoint: `GET /api/created-requisitions` (inline in `Program.cs`)
Query params:
- `page` (1-based, default 1), `pageSize` (default 20, capped at 100)
- `sortBy` ∈ {`supplierCode`,`item`,`description`,`quantity`,`date`,`requester`,`createdAt`}, `sortDir` ∈ {`asc`,`desc`}; default `createdAt desc`. Unsupported `sortBy` falls back to the default.
- Text filters (case-insensitive contains): `supplierCode`, `item`, `description`, `requester`
- Range filters: `minQuantity`/`maxQuantity`, `dateFrom`/`dateTo`, `createdFrom`/`createdTo`
- Session scope: `sessionId`; absent = all sessions.

Response: `{ items: CreatedRequisitionDto[], total, page, pageSize }`. `CreatedRequisitionDto` mirrors the entity fields (session id included, trivial parity with the agent-facing data).
- **Alternative considered:** separate endpoints for each filter dimension. Rejected — one query endpoint is simpler and composable.
- **Alternative considered:** GraphQL/OData. Rejected — overkill; the front-end needs one well-defined shape.

### 4. Front-end route and page
Add route `/requisitions` in `App.tsx` and a link in `AppSidebar`. `RequisitionsPage` owns query state (`page`, `sort`, column filter values, session scope) and converts it to the API query string on every change. Data is fetched on mount (refetch on navigation) and via a manual refresh button; requests are abortable so rapid filter edits don't race.
- **Alternative considered:** polling like `StatusPage`. Rejected — the user chose refetch-on-navigation + manual refresh.

### 5. Table: shadcn/ui `table` + minimal primitives
Add shadcn `table` (and any needed primitives) via the shadcn CLI. The page composes that with existing `Button`, `Input`, `Card`, `Dialog`, `Skeleton`, `ScrollArea`. Header cells render a sort button plus an inline filter input (and range inputs for quantity/date/created-at). Pagination is a simple prev/next + page indicator (no new dependency).
- **Alternative considered:** `@tanstack/react-table` data table. Rejected — sorting/filtering is server-side; a client-side table state manager adds dependency without value.

### 6. Session selector: two fixed options
A segmented two-option control ("All sessions" / "Current session"). "Current session" maps to `localStorage.prrag.session_id` at request time; if no session id is present the page falls back to "All sessions" (or an empty result set for an explicit current-session click, per spec). The control is a plain toggle, not a dropdown — the spec requires exactly two options.
- **Note:** after "New session" resets or discards the stored id, "Current session" reflects only the newest session; prior session-scoped filtering is unrecoverable from the UI (backend data unaffected).

### 7. Detail dialog: read-only
Clicking a row opens the existing shadcn `Dialog` showing all fields including the full description; no actions inside. Close restores the table.

## Risks / Trade-offs

- [**Session id loss on reset**] The stored session id is overwritten on "New session", so "Current session" only ever targets the active session → Acceptable: matches the two-option design; the backend keeps all history.
- [**Filter state dropped on navigation**] Filters/sort/pagination are component state, so leaving the page loses them → Mitigation: nothing to preserve by design; page reloads fresh per the chosen freshness behavior.
- [**Race between rapid filter edits**] Out-of-order responses could render stale rows → Mitigation: broad filters reset to page 1; implement as a single serialized request (abort previous) so the last change wins.
- [**Existing rows have no session**] Legacy requisitions are never attributable → Documented requirement; they appear under "All sessions" only.
- [**`session_id` nullable**] Column allows `NULL`; queries must use `WHERE session_id = @id` (null rows excluded) — null-safety in the EF query is handled by mapping the filter only when present.

## Migration Plan

Additive, non-breaking:
1. EF migration `AddCreatedRequisitionSession` adds nullable `session_id` to `created.created_requisitions`.
2. Auto-applied by `DbInitializer.ApplyMigrationsAsync` on API startup (existing behavior).
3. Rollback: drop the column (data loss only for session attribution, not for the requisitions themselves); application models revert via code.

## Open Questions

- Page size default/cap: proposal uses 20/100 — trivially adjustable.
- Whether "Current session" when no session exists should show all or empty: spec allows either; implementation will default to showing all (matches "no active session → fall back").