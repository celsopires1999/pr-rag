# Requisitions Page

## Purpose

Provide a dedicated web front-end page where users browse the purchase requisitions they created, with server-side pagination, sortable and filterable columns, a two-option session selector, a manual refresh button, and a read-only detail dialog. All UI copy is in English.

## ADDED Requirements

### Requirement: Dedicated requisitions page
The front-end SHALL present a dedicated Requisitions page, reachable from the sidebar, that lists created purchase requisitions in a table.

#### Scenario: Navigate to requisitions page
- **WHEN** the user clicks the Requisitions link in the sidebar
- **THEN** the application navigates to the requisitions page and displays the requisition table

#### Scenario: No requisitions exist
- **WHEN** the user opens the requisitions page and no requisitions have been created
- **THEN** the page shows an empty state instead of an empty table

### Requirement: Table with sortable columns
The page SHALL render requisitions in a table with columns for supplier code, item, description, quantity, date, requester, and creation time, where each column can be sorted ascending or descending.

#### Scenario: Sort by a column
- **WHEN** the user clicks a column header
- **THEN** the table re-sorts by that column, toggling direction on repeat clicks

#### Scenario: Default ordering
- **WHEN** the page loads
- **THEN** results are ordered by creation time descending unless the user changes the sort

### Requirement: Per-column filters
The page SHALL provide a filter control in each column header that narrows results to matching values.

#### Scenario: Apply a column filter
- **WHEN** the user enters a value in a column filter
- **THEN** the table shows only requisitions matching that value

#### Scenario: Clear a column filter
- **WHEN** the user clears a column filter
- **THEN** the table shows results without that filter applied

### Requirement: Server-side pagination
The page SHALL paginate requisitions server-side, showing a bounded set of rows per page with navigation between pages.

#### Scenario: Change page
- **WHEN** the user advances to another page
- **THEN** the table requests and shows the corresponding page of results

### Requirement: Session selector with fixed options
The page SHALL let the user scope results by session with exactly two fixed options: "All sessions" and "Current session".

#### Scenario: Show all sessions
- **WHEN** the user selects "All sessions"
- **THEN** results include requisitions from all sessions

#### Scenario: Show current session
- **WHEN** the user selects "Current session"
- **THEN** results are scoped to the active chat session id held by the application

#### Scenario: No active session
- **WHEN** the user selects "Current session" but no chat session has been started
- **THEN** the page falls back to showing all sessions or an empty result set

### Requirement: Manual refresh
The page SHALL fetch fresh data on navigation and provide a manual refresh control to re-request the current view.

#### Scenario: Refresh on navigation
- **WHEN** the user navigates to the requisitions page
- **THEN** the page loads the current data from the API

#### Scenario: Manual refresh
- **WHEN** the user clicks the refresh button
- **THEN** the page re-requests the current view, keeping applied filters, sort, and pagination

### Requirement: Read-only detail dialog
The page SHALL show a read-only detail dialog for a selected requisition, displaying all of its fields without offering create, edit, or delete actions.

#### Scenario: Open detail dialog
- **WHEN** the user clicks a requisition row
- **THEN** a dialog opens showing the full requisition details, including the complete description

#### Scenario: Close detail dialog
- **WHEN** the user closes the dialog
- **THEN** the dialog disappears and the table remains unchanged

### Requirement: English UI copy
All user-facing strings on the requisitions page SHALL be written in English.

#### Scenario: English labels and messages
- **WHEN** the requisitions page renders any label, message, or empty state
- **THEN** the text is in English