## 1. Repository layer

- [x] 1.1 Add `Task<bool> ExistsItemSupplierCombinationAsync(string item, string supplierCode, CancellationToken cancellationToken = default)` to `IPurchaseRequisitionRepository` (backend/src/PrRag.Application/Abstractions/IPurchaseRequisitionRepository.cs)
- [x] 1.2 Implement `ExistsItemSupplierCombinationAsync` in `PurchaseRequisitionRepository` using `.AnyAsync(x => x.Item == item && x.SupplierCode == supplierCode)` (backend/src/PrRag.Infrastructure/Services/PurchaseRequisitionRepository.cs)

## 2. ChatService guard

- [x] 2.1 Update the `create_requisition` tool registration description to state that creation is refused when no existing requisition has the same item + supplier combination (backend/src/PrRag.Application/Services/ChatService.cs)
- [x] 2.2 In `ChatService.CreateRequisitionAsync`, query `_repository.ExistsItemSupplierCombinationAsync(item, supplierCode, ct)` before calling `_requisitionWriter.WriteAsync`
- [x] 2.3 Return a clear error message (stating the supplier is not registered for that item and no requisition was created) and skip writing when the combination does not exist
- [x] 2.4 Update the base `SystemPrompt` tool description for `create_requisition` to mention the combination guard (backend/src/PrRag.Application/Services/ChatService.cs)

## 3. Skill guidance content

- [x] 3.1 Update `data/skills/create-purchase-requisition.md` so the validation step requires calling `search_by_codes` with the item and supplier together, confirming at least one requisition has that combination
- [x] 3.2 Add a guardrail in the skill stating the requisition CANNOT be created when the item + supplier combination has no existing requisition, regardless of individual code existence

## 4. Tests

- [x] 4.1 Add repository test: `ExistsItemSupplierCombinationAsync` returns true for a combo present in the DB and false for both individual and unknown combinations
- [x] 4.2 Add `ChatService` test: `create_requisition` with a valid line item whose item + supplier combination exists persists the file (extend `Confirmed_requisition_is_persisted_via_create_requisition`)
- [x] 4.3 Add `ChatService` test: `create_requisition` for an item + supplier combination not present in the DB writes no file and returns the "not registered for that item" error
- [x] 4.4 Add `ChatService` test: item and supplier each exist separately but never together still refuse creation (combination check is strict)
- [x] 4.5 Update `SkillsDirectoryTests`/`SkillFrameworkTests` fixture skill content to reference the combination validation (kept in sync with shipped skill)

## 5. Verify

- [x] 5.1 Run `dotnet build backend/PrRag.sln`
- [x] 5.2 Run `TEST_CONNECTION_STRING="Host=localhost;Port=5432;Username=prrag;Password=prrag" dotnet test backend/tests/PrRag.Tests`