## Context

The `create-purchase-requisition` skill drafts a new purchase requisition and persists it via the `create_requisition` tool in `ChatService`. Today the only validation is (a) the six required fields are present/valid and (b) the item and supplier codes each exist independently in the database (validated with `search_by_codes`).

Nothing checks that the specific **item code + supplier code combination** exists. So the agent could draft and persist a requisition for a supplier-item pair never seen before in `purchase_requisitions`. The purchasing workflow treats an item-supplier relationship as meaningful — a requisition only makes sense for suppliers already registered to supply that item.

The data model: each `PurchaseRequisition` row has `Item` and `SupplierCode`. There is no separate item or supplier table; the combination's "existence" is defined by whether any requisition row has the same `Item` AND same `SupplierCode`. This keeps the change lightweight — no schema changes or migration needed.

## Goals / Non-Goals

**Goals:**
- Before `create_requisition` persists anything, verify at least one `PurchaseRequisition` row has the exact same `Item` + `SupplierCode`.
- Refuse to create when the combination does not exist, returning a clear error.
- Update the shipped skill guidance so the assistant proactively checks the combination (by passing both codes to `search_by_codes` simultaneously) before finalizing the draft.
- Enforce the guard in code (not only prompt guidance) so behavior holds regardless of model adherence.

**Non-Goals:**
- No schema change or migration; no new tables (item/supplier catalogs).
- Not adding a new tool — reuse the existing `search_by_codes` for the skill guidance, and reuse `create_requisition` for the enforcement check.
- No frontend or API contract changes.
- No change to the `reports/` observability beyond what already exists.

## Decisions

### 1. Pre-flight check inside `CreateRequisitionAsync` (defense-in-depth)
The authoritative guard lives in `ChatService.CreateRequisitionAsync`, NOT just prompt/skill text. Before calling `_requisitionWriter.WriteAsync`, the service queries the repository for an existing requisition with the same `Item` + `SupplierCode`. If none, it returns an error string (which is what the tool hands back to the model) and writes nothing.

**Why**: The skill guidance is advisory content; a model may skip it. Enforcing in the tool guarantees the invariant regardless of what the model does. This is consistent with the existing pattern where the field-validation on `create_requisition` is enforced in `ChatService`/`IRequisitionWriter`, not prompt-only.

**Alternatives considered**: Enforce only in the skill markdown. Rejected — not reliable; guardrails must be code-enforced like the existing `create_requisition` validation.

### 2. New repository method `ExistsItemSupplierCombinationAsync`
Add `Task<bool> ExistsItemSupplierCombinationAsync(string item, string supplierCode, CancellationToken ct)` to `IPurchaseRequisitionRepository`, implemented in `PurchaseRequisitionRepository` as a `.AnyAsync(x => x.Item == item && x.SupplierCode == supplierCode)` on `DbSet<PurchaseRequisition>`.

**Why**: A dedicated boolean check is cheap (SQL `EXISTS`), clearer than reusing the retrieval-oriented `SearchByCodesAsync`, and keeps the combination semantics explicit in one place. It avoids building an EF expression from a result set.

**Alternatives considered**: Reuse `SearchByCodesAsync(new[] { item }, new[] { supplier }, 1)` and check non-empty. Rejected — couples a boolean existence check to a top-k retrieval projection and hides intent.

### 3. Unknown-combination error message
When the combination is missing, return a fixed, descriptive string to the model, e.g.: "Cannot create requisition: supplier SUP000001 has no recorded requisition for item ITM0001. No requisition was created. Ask the user to confirm the item and supplier." This same message is what the model surfaces to the user.

**Why**: The tool result is the only channel back to the user; a clear message prevents the model from re-attempting or inventing a fallback.

### 4. Skill markdown updated to require combination validation
In `data/skills/create-purchase-requisition.md`, the procedure step that validates codes now requires calling `search_by_codes` with **both** the item and supplier together (not separately), so the returned result proves at least one requisition has that combination. If the combined search returns no match, the assistant must warn the user the supplier is not registered for that item and ask to confirm/correct before finalizing the draft.

**Why**: This aligns the human-visible guidance with the code-enforced invariant and reduces the chance of a mid-flow rejection by `create_requisition`.

## Risks / Trade-offs

- **Model may call `search_by_codes` with item and supplier separately** → the enforcement in `CreateRequisitionAsync` still guarantees the invariant, so a bad code pair is caught at persist time even if the assistant skipped the combined check.
- **False rejection for a legitimately new item-supplier relationship** → by design; the workflow only supports suppliers already registered to supply the item. Documented in the skill so the assistant asks for confirmation rather than forcing through.
- **Backwards compatibility of the tool description** → the `create_requisition` tool description must mention the combination requirement so the model doesn't attempt it for an unverified pair and surface a needless error; the tool becoming stricter is acceptable since persistence is only permitted for known pairs.
- **Minimal query cost** → a `COUNT`/`EXISTS` per creation attempt is negligible; no indexes needed since equality on `Item` + `SupplierCode` is already covered by typical DB lookups.

## Migration Plan

No schema or data migration. Content change to the shipped skill file `data/skills/create-purchase-requisition.md` is picked up via the existing skills watcher/reload. The new repository method and `ChatService` guard go live on the next deploy; existing behavior is unchanged for contracts, only `create_requisition` becomes stricter.

## Open Questions

- Whether the error should also be observable in the RAG report (e.g. a "creation blocked" flag). Currently follows the existing pattern of surfacing via tool result only; can be added later without schema impact.