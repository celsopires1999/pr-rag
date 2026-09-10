## Context

The agent currently exposes four tools via `PurchaseRequisitionTools`. The two search tools (`search_by_codes`, `search_semantic`) return full `RagRetrievedItem` records that include requisition IDs, descriptions, and similarity scores, bounded by `TopK`. Users asking "which suppliers carry item X?" or "which items does supplier Y provide?" get requisition-centric results filtered through embedding search semantics — they don't need similarity scores, row limits, or requisition IDs; they need a clean, deduplicated list of Item + Supplier master pairs.

The existing `PurchaseRequisition` table already stores `SupplierCode`, `SupplierName`, `Item`, `ItemName` on every row. The same combination may appear across many requisition rows. A DISTINCT query on `(SupplierCode, Item)` gives the master-data view.

## Goals / Non-Goals

**Goals:**
- Add a `search_item_supplier_master` tool that returns distinct `(SupplierCode, SupplierName, Item, ItemName)` records.
- Accept optional `item` (string) and optional `supplier` (string) parameters; require at least one.
- Return all matching rows with no `TopK` limit.
- Integrate into the existing `PurchaseRequisitionTools` class following the established `RegisterFunction` pattern.
- Update the agent system prompt to document the new tool and when to prefer it.

**Non-Goals:**
- No new API endpoints — the tool is agent-internal only.
- No new EF Core migrations — queries the existing table.
- No frontend changes — results surface through the existing Markdown chat rendering.
- No pagination — the dataset is small (3k rows, <1k distinct pairs) and all results should be returned.
- No filtering by requisition fields (description, date, quantity).

## Decisions

### 1. New DTO: `ItemSupplierMasterResult`

Create a simple DTO in `PrRag.Application/DTOs/` with four fields: `SupplierCode`, `SupplierName`, `Item`, `ItemName`. This keeps the tool's return shape clean and distinct from `RagRetrievedItem` (which carries `PurchaseRequisitionId`, `Description`, `Similarity`).

**Alternative considered:** Reuse `RagRetrievedItem` with nulls for unwanted fields. Rejected — the tool's semantic purpose is different (master data vs. requisition search), and null-filled fields confuse the model's reasoning about what data is available.

### 2. Repository method: `SearchItemSupplierMasterAsync`

Add a method to `IPurchaseRequisitionRepository`:
```csharp
Task<IReadOnlyList<ItemSupplierMasterResult>> SearchItemSupplierMasterAsync(
    string? item,
    string? supplierCode,
    CancellationToken cancellationToken = default);
```

Implementation uses EF Core LINQ with `DistinctBy` or raw SQL `SELECT DISTINCT` to deduplicate on `(SupplierCode, Item)`. No `TopK` parameter — returns all matching rows.

**Alternative considered:** Query `PurchaseRequisition` entities and deduplicate in memory. Rejected — SQL `DISTINCT` is more efficient and avoids loading unnecessary columns (Description, Embedding).

### 3. Tool registration: single optional parameters (not lists)

The tool takes `string? item` and `string? supplier` (single values), not `IReadOnlyList<string>`. The use case is "all items for supplier X" or "all suppliers for item Y" — multi-value lookups are already handled by `search_by_codes`. Keeping single-parameter semantics simplifies the tool signature and makes the agent's decision about which tool to call clearer.

### 4. No `RecordToolCall` into `RetrievedItems`

The tool records itself via `RecordToolCall` (for the observability report), but does NOT append results to `_turnContext.RetrievedItems`. The retrieved items list is `RagRetrievedItem`-typed and feeds the `RagQueryReport` — mixing in master-data results would distort the report. The tool result is returned to the model directly via `FunctionResultContent`.

### 5. System prompt guidance

Add a fifth tool entry to `AgentInstructions.CoreInstructions`:
```
- search_item_supplier_master: use it when the user asks about the relationship between items and suppliers (which suppliers carry an item, which items a supplier provides) without needing requisition details. Returns distinct item-supplier pairs.
```

Update the "Allowed Actions" list to include the new tool name. Add a guardrail: prefer `search_item_supplier_master` over `search_by_codes` when the question is about item-supplier relationships, not about specific requisition records.

## Risks / Trade-offs

- **[Risk] Model confusion between `search_by_codes` and `search_item_supplier_master`** → Mitigated by clear prompt guidance distinguishing "requisition lookup" vs. "master data lookup". The tool descriptions themselves make the difference explicit.
- **[Risk] Large result sets overwhelming model context** → The dataset has <1k distinct pairs. Even a full return is manageable. If the dataset grows significantly, a `TopK` parameter can be added later without breaking changes.
- **[Trade-off] No similarity scores** → By design. The tool is for deterministic lookups, not semantic search. Users who need relevance ranking should use `search_semantic`.
