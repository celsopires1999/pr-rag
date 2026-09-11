## Context

The agent is composed via `AgentRunService` with a fixed tool list from `PurchaseRequisitionTools` (currently `search_by_codes`, `search_semantic`, `activate_skill`, `create_requisition`), each registered with `AIFunctionFactory.Create` on `[Description]`-annotated methods backed by `IPurchaseRequisitionRepository`. Agent behavior is steered by `AgentInstructions.CoreInstructions`, which lists each allowed tool in `<ALLOWED_ACTIONS>`. Data lives in `purchase_requisitions` (SupplierCode, SupplierName, Item, ItemName, Description), served by an EF Core repository. Reaching Postgres requires a real integration DB in tests; OpenAI is faked.

Today the agent answers "who supplies item X" questions only by inferring suppliers from `search_by_codes`/`search_semantic` output, which is unreliable for items with multiple suppliers. We add an explicit tool for this lookup.

## Goals / Non-Goals

**Goals:**
- Add a `get_suppliers_by_item` agent tool returning a distinct list of `{SupplierCode, SupplierName}` for an item given as an `ITM-*` code or item name.
- Ground the model's answer in data returned by the tool (consistent with the zero-hallucination guardrail).
- Record the tool call in the existing per-turn `ToolCalls` bookkeeping for the RAG observability report.
- Update the agent instruction prompt so the model knows to extract the item and call the tool.

**Non-Goals:**
- No changes to `search_by_codes`/`search_semantic` semantics.
- No database schema or EF migration changes (SupplierCode/SupplierName/Item already exist).
- No GUI/frontend changes.
- No caching of supplier lookups.

## Decisions

### D1: New tool `get_suppliers_by_item` registered on `PurchaseRequisitionTools`
The tool takes a single `item` parameter (string, the code or name) and returns a `IReadOnlyList<SupplierSummary>` DTO. Registered in the constructor's `RegisterFunction` block under the wire name `get_suppliers_by_item`, mirroring the existing four tools. This is the minimal, consistent extension of the existing tool mechanism.

Rationale: keeps all tools in one class with identical binding/recording semantics; no new tool-dispatch infrastructure needed.

Alternative considered: a generic `lookup_item` tool returning richer rows — rejected because the tool contract in a tool-using model is clearest when narrowly named after the question type ("suppliers for item"), matching the user's phrasing.

### D2: Repository method `GetSuppliersByItemAsync(item)` returning distinct supplier pairs
Add `Task<IReadOnlyList<SupplierSummary>> GetSuppliersByItemAsync(string item, CancellationToken)` to `IPurchaseRequisitionRepository`. EF Core implementation queries `PurchaseRequisitions` for rows whose `Item == item`, then projects and `Distinct()`s by `SupplierCode`/`SupplierName`. Dedup is exact on the code+name pair.

Rationale: matching on the exact `Item` column reuses the existing normalized `ITM-*` code; the sample data already stores `ITM-...` codes in `Item` while `ItemName` carries the human-readable label. Exact code matching is unambiguous and can be indexed, unlike fuzzy name matching.

Alternative considered: matching also by `ItemName` contains — rejected for this iteration because item names are not unique/stable identifiers; the prompt will tell the model to resolve non-code references to the code first (e.g., via `search_by_codes` on name). This keeps `get_suppliers_by_item` a precise, single-item lookup.

### D3: New DTO `SupplierSummary` (SupplierCode, SupplierName)
A small record/class in `PrRag.Application.DTOs`, returned by both the repository method and the tool. Keeps the Application layer free of EF/DB types and gives the model a clean JSON shape.

### D4: Prompt integration in `AgentInstructions.CoreInstructions`
Add a bullet in `<ALLOWED_ACTIONS>`: use `get_suppliers_by_item` when the user asks which suppliers provided/handled/sell a specific item, extracting the `ITM-*` code or item name from the question. Emphasize it returns the distinct supplier list, to be echoed without invention.

Rationale: the `_runOptions` already binds `tools.All` via `ChatToolMode.Auto`, so prompt guidance is the only steering the model needs to pick the tool.

### D5: Observability bookkeeping reuses `RecordToolCall`
The new handler calls `RecordToolCall("get_suppliers_by_item", ...)` exactly like the existing tools; no report schema change.

### D6: Test approach follows existing conventions
Update `AgentFrameworkLayeringTests.Purchase_requisition_tools_expose_the_four_fixed_tools` to five tools including the new name. Add repository-level integration tests (initial distinct behavior, duplicate rows collapsed, empty result) and a tool-level test verifying the returned suppliers via a `FakeChatClient` scripted to invoke the tool (pattern from `ItemSupplierCombinationTests`/`SkillFrameworkTests`).

## Risks / Trade-offs

- **Item name ambiguity** → `get_suppliers_by_item` matches on exact item code; if the model passes an item name, the lookup may return empty. Mitigation: prompt instructs the model to pass the `ITM-*` code; empty-result path answers "no supplier data for that item" per spec, degrading gracefully.
- **Unbounded result list** → a popular item could have many suppliers. Risk is low (the dataset is small); if it grows, a `LIMIT` can be added later. Explicitly deferred.
- **Tool-list spec drift** → both the proposal and the `maf-agent-integration` spec reference the exact tool count; forgetting to update tests/prompt breaks the contract. Mitigation: single tasks list updates instructions, tools class, repository, and tests in one change; the agent-framework-layering test asserts the full tool-name set, not just the count.

## Migration Plan

- No schema migration. The EF repository method is additive.
- After implementation, seed/re-ingest data as normal (existing rows already carry Item/SupplierCode/SupplierName).
- Rollback: remove the tool registration, the repository method, and the prompt bullet; no data changes to undo.

## Open Questions

- None blocking. (If exact item-name matching is later desired, it becomes a follow-up change rather than part of this one.)