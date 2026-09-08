## Context

The requisitions page (`frontend/src/pages/RequisitionsPage.tsx`) renders a 7-column server-side table. Each column header currently holds a sort button plus an always-visible filter control: a text input for the four text columns, two number inputs for quantity, and two side-by-side native `type="date"` inputs for both Date and Created at. Native date inputs have a large intrinsic width (~150px each), so the Date/Created-at columns make the header roughly twice as wide as the data columns and squeeze the table (inside an `overflow-x-auto` wrapper).

The page already debounces filter typing (300ms), keeps filters always enabled, and shows empty results as a full-width row inside the table body.

## Goals / Non-Goals

**Goals:**
- Slim column headers to roughly data width while keeping filters per-column and discoverable.
- Keep every filter reachable with a single click, with a visible "this column is filtered" signal.
- Reuse the existing backend query params and the existing debounce/abort/pagination logic untouched.
- Keep the "always render the header and filter entry points, always enabled" behavior the user asked for.

**Non-Goals:**
- No backend change; no change to sort, pagination, session scoping, refresh, or detail dialog.
- No hand-rolled date pickers or third-party calendar dependencies.
- No changes to the two-option session selector.

## Decisions

### D1: Radix popover per column, triggered by a filter button in the header

Each column header becomes two siblings: the existing sort button (column label) and a compact icon button (lucide `ListFilter`) that opens a Radix `Popover` containing that column's filter control(s). The controls are the existing ones (text input, min/max number, from/to date) — just moved out of the header row into the popover.

Rationale: matches the well-known AG-Grid / TanStack / Excel filter pattern; native date inputs stay (familiar, a11y-friendly); columns collapse to near-data width. Chosen because the user prioritized narrow columns over always-visible inputs (alternative B, a filter bar above the table, was rejected as it detaches filters from their columns).

### D2: Active-filter indicator + Clear action

For each column with a non-empty filter, the filter button renders with an accent style (e.g., `text-foreground`/primary dot) to signal activity. The popover footer has a "Clear" button that resets that column's filter keys and closes the popover. This replaces the previous "empty the inline input" clear mechanism.

Rationale: with controls hidden behind a popover, an aggregated visibility cue at the header is necessary; Clear avoids hunting for empty inputs.

### D3: Popover content is column-driven

A small per-column renderer (single function returning the control group for a `CreatedRequisitionSortField`) produces the popover body: text input for `supplierCode|item|description|requester`, min/max numbers for `quantity`, from/to dates for `date` (keys `dateFrom`/`dateTo`) and `createdAt` (keys `createdFrom`/`createdTo`). Debounce, reset-to-page-1, abort-on-change behavior is unchanged because the popover writes to the same `Filters` state.

### D4: Add shadcn `popover` component

Add via the project's shadcn CLI (`npm run shadcn add popover`, style must match the existing radix-nova components) so the popover uses the same primitives and theme as the existing `dialog`, `sheet`, and `tooltip`. No new external runtime dependency beyond what shadcn brings (`radix-ui` popover, already a transitive peer in the project).

### D5: Header stays slim; empty/loading states unchanged

The header row renders only labels + sort + filter buttons, so columns never exceed data width (monospace cells truncate). The body skeleton/empty-row/row logic, pagination footer, and the always-enabled filter buttons are preserved.

## Risks / Trade-offs

- [Filters are now one click away instead of always visible] → Active-filter accent on the button + single-click open keeps them discoverable; the popover auto-focuses its input.
- [Popover overlap on a horizontally scrollable table] → Popovers anchor to their column and the table scrolls horizontally independent of the popover; long date controls fit above the row area so they do not collide with the footer.
- [More motion: open/close popover per edit] → Native inputs re-render live as the user types; changes apply immediately (debounced) without needing to close the popover.
- [New shadcn component must match theme] → Added via the same CLI used for the existing audit; verified by `npm run lint`/`npm run build`.

## Migration Plan

No backend or data migration. Frontend-only: add `popover` component, rework the header, delete the old inline `FilterInput` controls from the header, rebuild + lint. Rollback is a revert of `RequisitionsPage.tsx` and the `ui/popover.tsx` addition.