## 1. UI component

- [x] 1.1 Add shadcn `popover` component via the CLI with the same radix-nova style used by the existing UI components (`frontend/src/components/ui/popover.tsx`)
- [x] 1.2 Verify the popover imports match the project's alias (`@/lib/utils`) and no stray package-lock changes remain

## 2. Filter controls moved into popovers

- [x] 2.1 Refactor `FilterInput` in `frontend/src/pages/RequisitionsPage.tsx` into a per-column renderer that returns the column's filter control group (text input, quantity min/max, date from/to, created-at from/to) for use inside a popover body
- [x] 2.2 Rebuild the column header: keep the sort button (label + direction chevron) and add a compact filter button (lucide `ListFilter`) that toggles the popover containing that column's controls
- [x] 2.3 Collect the filter controls into popover content with label per row and a "Clear" action that resets that column's filter keys and closes the popover
- [x] 2.4 Render an active-filter indicator on the filter button when any of that column's filter keys is non-empty
- [x] 2.5 Ensure the "always available" behavior: every header filter button stays visible and enabled during loading, during the filter debounce, and when the result set is empty (header and empty-state row logic unchanged)
- [x] 2.6 Confirm date/created-at columns no longer grow the header width (slim headers; only label + sort + filter button)

## 3. Verification

- [x] 3.1 Run `npm run build` and `npm run lint` in `frontend/` successfully; no new lint warnings from `RequisitionsPage.tsx`
- [x] 3.2 Manual smoke check in the running app: open a column popover, type a filter (debounced request fires), clear it, and confirm data filtering, page reset, and empty-result behavior still work
- [x] 3.3 Confirm no backend changes were needed (query params unchanged) and `dotnet test` still passes as a sanity check if the API image is rebuilt