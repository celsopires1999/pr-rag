# Requisitions Page

## Purpose

Provide a dedicated web front-end page where users browse the purchase requisitions they created, with server-side pagination, sortable and filterable columns, a two-option session selector, a manual refresh button, and a read-only detail dialog. All UI copy is in English.

## MODIFIED Requirements

### Requirement: Per-column filters
The page SHALL provide a filter control for each column, opened from a filter button in the column header, that narrows results to matching values. The header and its filter buttons SHALL be rendered and enabled at all times, including while loading, during debounced typing, and when the result set is empty.

#### Scenario: Open a column filter
- **WHEN** the user clicks a column's filter button in the header
- **THEN** a popover opens with that column's filter control (text input for supplier code, item, description, and requester; min–max fields for quantity; from–to fields for date and creation time)

#### Scenario: Apply a column filter
- **WHEN** the user enters a value in a column filter control
- **THEN** the table shows only requisitions matching that value once the debounced request completes

#### Scenario: Indicate an active filter
- **WHEN** a column has an applied filter
- **THEN** the column's filter button shows an active-filter indicator

#### Scenario: Clear a column filter
- **WHEN** the user clears a column's filter using the popover's clear action
- **THEN** the table shows results without that filter applied and the indicator is removed

#### Scenario: Filter controls always available
- **WHEN** the table is loading, is showing results, or shows an empty result set
- **THEN** every column header's filter button remains visible and enabled