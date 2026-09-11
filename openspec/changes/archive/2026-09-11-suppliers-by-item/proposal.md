## Why

The purchase-requisition agent can match requisition rows by item/supplier codes, but it has no direct way to answer supplier-lookup questions such as "What are the suppliers that provided the item Hydraulic Oil?" or "Give me the suppliers for item ITM-00000000000000000007". Today the model would have to infer suppliers from item-detail search results, which is unreliable when the same item has multiple suppliers. A dedicated tool makes the agent ground its answer in an explicit, de-duplicated supplier list.

## What Changes

- Add a new agent tool `get_suppliers_by_item` that takes an item identifier (item code or item name) and returns the list of suppliers (SupplierCode + SupplierName) that supplied that item.
- The tool returns a distinct list: each supplier appears exactly once even when multiple requisitions reference the same item + supplier combination.
- Add the repository query that fetches distinct supplier codes/names for a given item from the purchase-requisition store.
- Register the tool in the agent's fixed tool list and document it in the agent instructions (`<ALLOWED_ACTIONS>`), so the model knows to extract the item from the question and call the tool.
- No change to chat response shape or observability report contract; the new tool call is recorded in the per-turn `ToolCalls` bookkeeping like the existing tools.

## Capabilities

### New Capabilities
- `suppliers-by-item`: Agent capability to answer "who supplies this item" questions — a `get_suppliers_by_item` tool that returns the distinct suppliers (code + name) for an item extracted from the user's question.

### Modified Capabilities
- `maf-agent-integration`: The MAF tool registration requirement currently fixes the tool list at four functions (`search_by_codes`, `search_semantic`, `activate_skill`, `create_requisition`); it changes to cover the new fifth tool `get_suppliers_by_item` registered through the same `AIFunctionFactory` mechanism.

## Impact

- `PrRag.Application`:
  - `PurchaseRequisitionTools` — new `get_suppliers_by_item` handler registered in the fixed tool list.
  - `IPurchaseRequisitionRepository` — new `GetSuppliersByItemAsync` method.
  - `AgentInstructions.CoreInstructions` — `get_suppliers_by_item` entry in `<ALLOWED_ACTIONS>`.
  - New DTO for the supplier result (SupplierCode, SupplierName).
- `PrRag.Infrastructure`:
  - `PurchaseRequisitionRepository` — EF Core query returning distinct supplier code/name for a matching item.
- `PrRag.Tests`:
  - Update `AgentFrameworkLayeringTests` tool-count assertion; add coverage for the distinct supplier query and the tool handler.
- No database schema or migration changes (data already contains SupplierCode/SupplierName/Item).