## Why

The `create-purchase-requisition` skill lets the assistant draft and persist a new purchase requisition, validating that the item and supplier codes individually exist in the retrieval index before calling `create_requisition`. However, existence of the item and the supplier independently does not guarantee that the **combination** of that item + supplier has any prior requisition record. Creating a requisition for a supplier-item pair never seen before would silently register a relationship the purchasing workflow does not support. Before persisting, we must verify that at least one entry with the same item code + supplier code combination already exists in the database; if it does not, the purchase requisition must not be created.

## What Changes

- Add a mandatory pre-flight check before `create_requisition` persists a requisition: the system SHALL verify that at least one purchase requisition in the database has the exact same item code + supplier code combination as the one about to be created.
- If the combination does not exist, `create_requisition` SHALL refuse to write and return an error explaining that the supplier is not registered for that item (and that no requisition was created).
- The `create-purchase-requisition` skill guidance SHALL be updated so the assistant performs this combination check (via the existing `search_by_codes` exact-code search) as part of validating codes before drafting — not just individual item/supplier existence.
- **BREAKING**: none at the API level — `POST /api/chat`/`/api/chat/stream` contracts are unchanged. Behavior of the `create_requisition` tool changes (may now refuse for an unknown item-supplier pair), which is internal consumption by the agent.

## Capabilities

### New Capabilities

- `purchase-requisition-creation-guard`: the requirement and behavior guard that a purchase requisition can only be created when the item code + supplier code combination already exists in the database.

### Modified Capabilities

- `skill-framework`: the `create_requisition` tool and the `create-purchase-requisition` skill SHALL enforce the item-supplier combination existence check before persisting.

## Impact

- **PrRag.Application**: `ChatService.CreateRequisitionAsync` SHALL query the repository for an existing item+supplier combination before delegating to `IRequisitionWriter`; repository abstraction gains a method to check the combination; `create_requisition` description updated to tell the model it refuses unknown combinations.
- **PrRag.Infrastructure**: `PurchaseRequisitionRepository` implements the item+supplier combination existence query (exact match on `Item` and `SupplierCode`).
- **PrRag.Tests**: new scenarios for `create_requisition` refusing an unknown item-supplier pair and succeeding when the pair exists; updated skill guidance test.
- **data/skills/create-purchase-requisition.md**: procedure updated to validate the item+supplier combination via `search_by_codes` before finalizing the draft.
- **Frontend**: no change — error surfaces as a normal assistant message.