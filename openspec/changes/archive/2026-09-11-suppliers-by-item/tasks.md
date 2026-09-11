## 1. Data layer

- [x] 1.1 Add `SupplierSummary` DTO (SupplierCode, SupplierName) in `PrRag.Application/DTOs`
- [x] 1.2 Add `GetSuppliersByItemAsync(string item, CancellationToken)` to `IPurchaseRequisitionRepository`
- [x] 1.3 Implement `GetSuppliersByItemAsync` in `PurchaseRequisitionRepository` returning distinct SupplierCode/SupplierName rows for exact `Item` match

## 2. Tool registration

- [x] 2.1 Add `get_suppliers_by_item` handler to `PurchaseRequisitionTools` (single `item` parameter, returns `IReadOnlyList<SupplierSummary>`), registered via `RegisterFunction` and recorded through `RecordToolCall`
- [x] 2.2 Add `get_suppliers_by_item` entry to `<ALLOWED_ACTIONS>` in `AgentInstructions.CoreInstructions` instructing the model to extract the ITM-* code (or item name) from the question and echo the returned distinct supplier list without inventing entries

## 3. Tests

- [x] 3.1 Update `AgentFrameworkLayeringTests.Purchase_requisition_tools_expose_the_four_fixed_tools` to assert the five-tool set including `get_suppliers_by_item`
- [x] 3.2 Add repository integration tests: distinct suppliers returned for an item with multiple suppliers, duplicate item+supplier rows collapsed to one entry, and empty list for an unknown item
- [x] 3.3 Add tool/agent test scripting `get_suppliers_by_item` via `FakeChatClient` (pattern from `ItemSupplierCombinationTests`) verifying the tool returns the correct supplier list

## 4. Verify

- [x] 4.1 Run `dotnet build backend/PrRag.sln`
- [x] 4.2 Run `dotnet test backend/tests/PrRag.Tests` against Postgres (via `TEST_CONNECTION_STRING` or compose `--profile test`)