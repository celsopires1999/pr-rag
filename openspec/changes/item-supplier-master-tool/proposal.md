## Why

The existing `search_by_codes` tool mixes master data lookups with requisition search semantics (similarity scores, row limits, embedding-based retrieval). Users need a dedicated tool to answer questions like "Which suppliers carry item ITM-000000001?" or "Which items does supplier SUP00001 provide?" without the overhead or noise of requisition-centric search. A dedicated master-data tool provides deterministic, unfiltered results with a clear SupplierCode / SupplierName / Item / ItemName projection.

## What Changes

- Add a new `search_item_supplier_master` tool to the agent toolset that queries the `PurchaseRequisition` table for distinct Item + Supplier combinations.
- The tool accepts optional `item` (item code) and optional `supplier` (supplier code) parameters; at least one must be provided.
- Results return as a flat list of `ItemSupplierMasterResult` records (SupplierCode, SupplierName, Item, ItemName) with no row limit and no similarity score.
- The tool does NOT surface requisition-specific fields (PurchaseRequisitionId, Description, Embedding, Similarity).
- The agent system prompt is updated to document the new tool and when to prefer it over `search_by_codes`.

## Capabilities

### New Capabilities

- `item-supplier-master-tool`: Dedicated agent tool for querying distinct Item + Supplier master data relationships, independent of requisition search semantics.

### Modified Capabilities

- `maf-agent-integration`: Agent prompt and tool list updated to include the new tool with guidance on when to use it.

## Impact

- **Application layer**: New DTO (`ItemSupplierMasterResult`), new repository method on `IPurchaseRequisitionRepository`, new tool handler in `PurchaseRequisitionTools`.
- **Agent prompt**: `AgentInstructions.CoreInstructions` updated to describe the new tool and usage rules.
- **Tests**: New integration tests for the repository method and tool invocation.
- **No API endpoint changes**: The tool is invoked only via the agent; no new HTTP surface.
- **No schema/migration changes**: Queries the existing `purchase_requisitions` table.
- **No frontend changes**: Tool results are surfaced through the existing chat Markdown rendering pipeline.
