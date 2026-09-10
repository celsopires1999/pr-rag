## 1. DTO

- [x] 1.1 Create `ItemSupplierMasterResult` record in `PrRag.Application/DTOs/` with fields: `SupplierCode`, `SupplierName`, `Item`, `ItemName`

## 2. Repository

- [x] 2.1 Add `SearchItemSupplierMasterAsync` method to `IPurchaseRequisitionRepository` interface
- [x] 2.2 Implement `SearchItemSupplierMasterAsync` in `PurchaseRequisitionRepository` using EF Core LINQ with `DistinctBy` on `(SupplierCode, Item)`, filtering by optional `item` and `supplierCode` parameters

## 3. Tool

- [x] 3.1 Add `search_item_supplier_master` tool handler and registration in `PurchaseRequisitionTools`
- [x] 3.2 Record tool call via `RecordToolCall` without appending to `_turnContext.RetrievedItems`

## 4. Agent prompt

- [x] 4.1 Add `search_item_supplier_master` entry to `AgentInstructions.CoreInstructions` Tools list
- [x] 4.2 Add guidance on when to prefer `search_item_supplier_master` over `search_by_codes`

## 5. Tests

- [x] 5.1 Add integration test for `SearchItemSupplierMasterAsync` repository method (query by item, by supplier, by both, no params, no results)
- [x] 5.2 Verify build passes: `dotnet build backend/PrRag.sln`
- [x] 5.3 Verify tests pass: `dotnet test backend/tests/PrRag.Tests`
