## 1. Tool result contracts

- [x] 1.1 Create `PrRag.Application/Services/Agents/ToolResults.cs` with `ToolRequisitionHit` (requisitionId, supplierCode, supplierName, item, itemName, description, similarity) and `ToolSearchResult` (count, items), every property carrying explicit `[JsonPropertyName]` camelCase and a `[Description]`
- [x] 1.2 Add `ToolSupplierHit` (supplierCode, supplierName) and `ToolSupplierList` (count, suppliers) to `ToolResults.cs`, with the same attributes
- [x] 1.3 Add `ToolSkillActivation` (name, found, body, message) and `ToolRequisitionWrite` (success, requisitionId, message) to `ToolResults.cs`, with the same attributes
- [x] 1.4 Add static mappers on the result types that build them from `RagRetrievedItem` and `SupplierSummary`, so the report DTO and the tool result DTO are populated from the same domain data independently

## 2. Tool binding

- [x] 2.1 Add a public const per tool wire name to `PurchaseRequisitionTools` (`SearchByCodesTool`, `SearchSemanticTool`, `ActivateSkillTool`, `CreateRequisitionTool`, `GetSuppliersByItemTool`) and use each const in both registration and `RecordToolCall`, deleting the duplicated string literals
- [x] 2.2 Delete the five `private const string *Description` fields and stop passing a description to `AIFunctionFactoryOptions`, so the handler's `[Description]` attribute is the single source of the tool description
- [x] 2.3 Change `RegisterFunction` to `RegisterFunction(string wireName, Delegate handler)`, removing the passthrough `MarshalResult` so the default JSON marshalling applies
- [x] 2.4 Register each tool with its `[Description]`-annotated method group instead of the forwarding lambda, and move the `= null` defaults for `SearchByCodesAsync`'s `items`/`suppliers` onto the handler signature so they stay optional
- [x] 2.5 Inject `ILogger<PurchaseRequisitionTools>` and add a structured log per invocation (tool name, supplied argument names, elapsed ms, result count), keeping `RecordToolCall` for report bookkeeping

## 3. Handler return types

- [x] 3.1 Return `ToolSearchResult` from `SearchByCodesAsync`, still appending the mapped `RagRetrievedItem` list to the turn context for the report
- [x] 3.2 Return `ToolSearchResult` from `SearchSemanticAsync`, preserving the `RewrittenQuery` assignment and turn-context bookkeeping
- [x] 3.3 Return `ToolSupplierList` from `GetSuppliersByItemAsync`
- [x] 3.4 Return `ToolSkillActivation` from `ActivateSkillAsync` with `found` true/false, the skill body on success, and the existing "Unknown skill ... Available skills: ..." text preserved verbatim in `message` on failure
- [x] 3.5 Return `ToolRequisitionWrite` from `CreateRequisitionAsync` with `success` true/false, the created id on success, and the existing validation, unregistered-combination, and writer-error messages preserved verbatim in `message`
- [x] 3.6 Update the class XML doc, which still says "The four purchase-requisition tools"

## 4. Tests

- [x] 4.1 Add `ToolSchemaTests` asserting every tool in `PurchaseRequisitionTools.All` has a non-empty `AIFunction.Description` and a non-null JSON schema
- [x] 4.2 In `ToolSchemaTests`, assert every non-cancellation-token parameter in each tool's generated schema has a non-empty `description`, and that `search_by_codes` has an empty `required` array — the regression guard for the lambda-binding defect
- [x] 4.3 In `ToolSchemaTests`, assert a scripted tool invocation's `FunctionResultContent.Result` arrives as JSON text containing the expected camelCase keys, proving the OpenAI boundary still receives serialized data
- [x] 4.4 Update `AgentFrameworkLayeringTests.Purchase_requisition_tools_expose_the_fixed_tools` to assert the five names against the new public consts instead of re-typed literals
- [x] 4.5 Update `GetSuppliersByItemTests.Get_suppliers_by_item_tool_returns_the_distinct_supplier_list` to assert the counted supplier-object shape (camelCase `supplierCode` keys and `count`) instead of raw supplier strings
- [x] 4.6 Update `SkillFrameworkTests.ExtractRequisitionId` to read the `requisitionId` field from the tool result instead of scraping the prose marker, and confirm the four prose `Assert.Contains` checks still pass
- [x] 4.7 Review `ItemSupplierCombinationTests`, `RagObservabilityReportTests`, and `AgenticRetrievalTests` for assumptions about the old bare-array/bare-string result shapes and update as needed

## 5. Documentation

- [x] 5.1 Correct the two stale `AGENTS.md` entries: the `[Skill: <skill-name>]` answer marker prefixed by `ChatService.ApplySkillMarker` (removed in the MAF migration; skill state now lives in `AgentSession.StateBag` via `SkillSessionState`) and `FakeQueryRewriter` (removed by the `2026-09-05-remove-semantic-query-rewriter` change)
- [x] 5.2 Note in `AGENTS.md`'s gotchas that adding a tool means editing the handler, the shared wire-name const, the `<ALLOWED_ACTIONS>` prompt block, and the tool-name assertion, and that a tool's parameter descriptions only reach the model when the handler method itself is registered

## 6. Verify

- [x] 6.1 Build the solution in the devcontainer as the `vscode` user: `docker compose exec -u vscode -w /workspaces/backend devcontainer dotnet build /workspaces/backend/PrRag.sln -c Debug` — build succeeded, 0 errors, no C#/xUnit warnings (4 pre-existing NU1903 for `Microsoft.OpenApi` 2.0.0)
- [x] 6.2 Run the test suite against Postgres and confirm all existing agentic tests pass unchanged where they assert on preserved message text: `docker compose exec -u vscode -w /workspaces/backend devcontainer dotnet test /workspaces/backend/tests/PrRag.Tests/PrRag.Tests.csproj` — 64/64 passed
- [x] 6.3 Validate the change: `openspec validate harden-agent-tool-contract --strict` — `Change 'harden-agent-tool-contract' is valid`
- [x] 6.4 Re-run the demo flow with a real OpenAI key (`docker compose --profile demo up`) and walk the create-requisition path to confirm the model still drives the skill, the code validation, the confirmation gate, and the persisted requisition under the new result shapes — closed by the live walkthrough recorded in "Live demo findings" below; the two behaviours that were open when this task was written were fixed and re-verified by the follow-up change `enforce-requisition-confirmation-gate` (archived 2026-09-25)

### Live demo findings (6.4, partial)

Verified against a freshly rebuilt `api` image (the pre-existing `prrag-api` image on this box was
stale, built 2026-09-06, and had to be rebuilt for the run to be meaningful):

- `get_suppliers_by_item` returns 10 distinct suppliers for `ITM-00000000000000000002`; the model
  read the new `ToolSupplierList` shape correctly.
- `search_semantic` runs multi-step (2 calls in one turn) and feeds `RetrievedCount: 10`.
- `create_requisition` persists to `created.created_requisitions` and the model quotes the UUID
  returned by the new `ToolRequisitionWrite` shape.
- The report schema is byte-for-byte unchanged (PascalCase keys, `RetrievedItems`, `ToolCalls`).
- Dumped the live tool contract: all five tool descriptions come from the method `[Description]`
  verbatim, and every parameter now carries a description in the JSON schema.

Not verified at the time, and **pre-existing rather than a regression**: the model did not call
`activate_skill` for "I need to create a purchase requisition" (3/3 runs) and it called
`create_requisition` without an explicit confirmation turn. Both were reproduced identically on
`main` with the change stashed and the image rebuilt, so they were gpt-4o-mini prompt-adherence
issues outside the scope of this change. The confirmation gate in 6.4 was therefore unexercised.

### Resolution of the two open items (6.4)

Both open items turned out to be real and were fixed by `enforce-requisition-confirmation-gate`,
which was implemented after this change and archived on 2026-09-25. Re-verified live against a
freshly built image:

- **Skill activation.** The positive skill description plus a mandatory `SkillsGuide` in the system
  prompt took activation from 2/6 to 6/6 on natural phrasings, including the exact sentence that
  previously failed ("I need to create a purchase requisition"). Confirmed the manifest, skill
  loader, and per-turn re-injection were all already correct — the miss was purely the description
  the model routed on.
- **Confirmation gate.** Now enforced in code, not just by prompt: `create_requisition` refuses
  without a confirmed draft. An adversarial turn instructing the model to skip both the draft and the
  confirmation was refused and wrote nothing (`count(*) where quantity = 7` → 0); the model then
  self-corrected within the same turn and asked for confirmation. The happy path persists only after
  a real user confirmation turn.
- **Result shapes under the new gate.** The three draft/confirmation tools and the structured
  `missingStep` refusal shape were exercised live, and `create_requisition` still returns the
  `ToolRequisitionWrite` UUID that the model quotes.

So 6.4 is now satisfied end to end: the model drives the skill, the code validation runs, the
confirmation gate holds, and the requisition persists — with the `ToolRequisitionWrite` shape
unchanged. Nothing in this change was regressed by the follow-up; it was extended.
