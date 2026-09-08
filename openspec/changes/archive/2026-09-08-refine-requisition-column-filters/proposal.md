## Why

The per-column date and creation-time filters render two side-by-side native date inputs (each ~150px) in the column header, ballooning the header width and squeezing the table so badly that the columns become unusably wide. The filters need to stay discoverable and per-column without consuming that much horizontal space.

## What Changes

- Replace the inline filter inputs in the requisitions-table column headers with **popover-based column filters**: a small filter button in each column header opens a popover holding that column's filter controls (text input for supplier code / item / description / requester, min–max numbers for quantity, from–to dates for date and created-at).
- Add an **active-filter indicator** on a column's filter button when that column has an applied filter, and a **Clear** action inside the popover.
- Keep the header and filter entry points **always rendered and always enabled**, including while loading, during the filter debounce, and when the result set is empty — the current row-based empty state stays.
- Keep sort behavior unchanged (click header label to sort; the filter button is a separate control).
- Sorting, pagination, session scoping, and the API query contract are unchanged.

## Capabilities

### New Capabilities

- none

### Modified Capabilities

- `requisitions-page`: the per-column filter requirement changes from an always-visible inline input to a popover triggered by a column-header filter button, with an active-filter indicator and a clear action.

## Impact

- `frontend/src/pages/RequisitionsPage.tsx` — header layout reworked (slim columns, filter buttons), filter controls moved into per-column popovers.
- `frontend/src/components/ui/` — shadcn `popover` component added via the CLI.
- No backend or API changes; the existing `GET /api/created-requisitions` query params are reused unchanged.