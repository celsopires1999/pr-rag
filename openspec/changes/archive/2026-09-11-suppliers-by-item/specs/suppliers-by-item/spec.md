## ADDED Requirements

### Requirement: Item supplier lookup tool
The system SHALL expose a `get_suppliers_by_item` tool to the purchase-requisition agent that accepts an item identifier (an `ITM-*` item code or an item name) and SHALL return the list of suppliers that supplied that item, each supplier appearing exactly once regardless of how many requisitions reference the same item + supplier combination.

#### Scenario: Question includes the exact item code
- **WHEN** the user asks "Give me the suppliers for item ITM-00000000000000000007"
- **THEN** the agent extracts `ITM-00000000000000000007` and calls `get_suppliers_by_item`, and the tool returns every distinct SupplierCode + SupplierName recorded for that item

#### Scenario: Question names the item without a code
- **WHEN** the user asks "What are the suppliers that provided the item Hydraulic Oil?"
- **THEN** the agent resolves the item reference and calls `get_suppliers_by_item`, and the tool returns the distinct suppliers for the matching item

#### Scenario: Duplicate item + supplier pairs are removed
- **WHEN** multiple requisitions exist for the same item supplied by the same supplier
- **THEN** the tool returns that supplier only once in the result list

#### Scenario: No suppliers exist for the item
- **WHEN** the requested item has no recorded requisitions
- **THEN** the tool returns an empty list and the agent answers that it has no supplier data for that item

#### Scenario: Tool call recorded for observability
- **WHEN** `get_suppliers_by_item` is invoked during a chat request
- **THEN** the call is recorded in the same per-turn `ToolCalls` bookkeeping used by the other tools, without changing the public response body