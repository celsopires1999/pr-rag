## Context

`PurchaseRequisitionTools` (Application layer) is the single definition site for the agent's five tools. It builds `AIFunction` instances in its constructor and hands the list to `AgentRunService`, which binds it to `ChatOptions.Tools` with `ChatToolMode.Auto`. MAF owns the ReAct loop, so tool binding is the only lever this project has on the contract the model sees.

Three defects in that binding:

1. **Parameter descriptions never reach the model.** Each tool is registered with a forwarding lambda — `(string query, CancellationToken ct) => SearchSemanticAsync(query, ct)` — while the `[Description]` attributes live on the private handler methods' parameters. `AIFunctionFactory` derives the JSON schema from `MethodInfo.GetParameters()` of the delegate it is handed, so it reads the lambda's parameters, which carry no attributes. Every parameter description in the codebase is dead metadata. This silently defeated commit `74909c3`.
2. **Tool descriptions are declared twice.** Each description exists as a `private const string` *and* as a `[Description(...)]` attribute, with `RegisterFunction` passing the const via `AIFunctionFactoryOptions.Description`, which overrides the attribute. Two sources of truth, no compile-time link.
3. **Results are untyped passthroughs.** `RegisterFunction` sets `MarshalResult = (result, _, _) => new(result)`, so `FunctionResultContent.Result` holds the raw `IReadOnlyList<RagRetrievedItem>` / `IReadOnlyList<SupplierSummary>` rather than a declared shape. Those types use PascalCase property names, carry `Similarity = null` noise, and — critically — return a bare array with no count, so the model cannot distinguish "no matches" from a malformed payload. `activate_skill` and `create_requisition` return bare prose strings that overload success and failure into the same channel.

Wire names are also duplicated per tool: once in `RegisterFunction` and again in `RecordToolCall`, five times over.

Constraints: Application layer stays free of Infrastructure dependencies; tools are `Scoped` because the handlers capture `AgentTurnContext` for `top_k`/`min_similarity` and report bookkeeping; the RAG observability report schema (`RagQueryReport`, `RagRetrievedItem`, `RagToolCall`) is consumed by tooling and is frozen; the shipped skill markdown and `AgentInstructions.CoreInstructions` reference tools by their exact wire names.

## Goals / Non-Goals

**Goals:**

- Make the generated tool schema contain a description for every parameter, with a regression test that fails if it regresses.
- One declaration site per tool description and per tool wire name.
- Give the model an explicit, self-describing result contract: camelCase keys, a count, and success/found flags instead of prose-encoded outcomes.
- Structured logging for tool invocations, replacing the ad-hoc console tracing style while keeping the report bookkeeping intact.
- Keep the model-facing payload decoupled from the observability report schema.

**Non-Goals:**

- No new tools, no tool removed. The set stays at five.
- No change to the tool set or wording in `AgentInstructions.CoreInstructions`, or in the shipped skill markdown — the wire names are preserved exactly.
- No change to repository, embedding, skill, or requisition-writer behavior. The handlers call the same methods with the same arguments and apply the same per-turn parameters.
- No MCP, no `FunctionInvokingChatClient`, no agent middleware, no splitting the class into per-tool providers.
- No change to the observability report JSON shape, the chat response body, the API surface, or the frontend.

## Decisions

### D1: Register the annotated method group, not a forwarding lambda

`RegisterFunction` takes the handler as a method group (`SearchSemanticAsync`) and C# infers the natural delegate type `Task<T>(string, CancellationToken)`. Reflection then sees the real `MethodInfo`, so the parameter `[Description]` attributes are visible to `AIFunctionFactory`.

Rationale: this is the actual fix. Everything else in this change is hygiene around it.

Alternative considered: keep the lambdas and copy parameter descriptions onto the lambda parameters — rejected, it doubles the duplication this change exists to remove. Alternative: build the schema by hand via `AIFunctionFactoryOptions.Schema` — rejected, it discards type inference and re-implements `AIFunctionFactory`.

**Verified constraint:** `AIFunctionFactory` reads `[Description]` off the method when `options.Description` is null, so dropping the consts does not silently blank the tool description. Covered by a test.

**Verified constraint:** `MethodInfo.GetParameters()` preserves `HasDefaultValue`/`DefaultValue` across the method-group conversion, so `search_by_codes`'s optional `items`/`suppliers` stay out of the schema's `required` array once the defaults move onto the handler signature. Covered by a test.

### D2: `[Description]` on the handler is the only tool description

Delete the five `*Description` consts and stop setting `options.Description`. `RegisterFunction` becomes `(string wireName, Delegate handler)`.

Rationale: matches the reference pattern, and removes the possibility of the two sources disagreeing. `Name` must stay explicit because the wire names are `snake_case` and are referenced by the prompt, the shipped skill, and the report.

### D3: Wire names as public consts on the tools class

```csharp
public const string SearchByCodesTool = "search_by_codes";
```

consumed by both `RegisterFunction` and `RecordToolCall`. Tests assert against the consts instead of re-typing literals.

Rationale: a rename is now a single edit that cannot drift the report away from the tool actually exposed. `AgentInstructions.CoreInstructions` stays literal text — interpolating consts into a `const` string is impossible and templating the prompt adds indirection for no safety gain, since the layering test asserts the tool set.

Alternative considered: pass the `AIFunction.Name` into each handler — rejected, it changes every handler signature and couples business logic to the binding layer.

### D4: Dedicated result DTOs, decoupled from the report DTOs

New `Services/Agents/ToolResults.cs` with six POCOs, each property carrying `[JsonPropertyName]` (explicit camelCase) and `[Description]`:

| Type | Shape | Replaces |
|---|---|---|
| `ToolRequisitionHit` | `requisitionId, supplierCode, supplierName, item, itemName, description, similarity` | `RagRetrievedItem` on the model path |
| `ToolSearchResult` | `count, items[]` | bare `IReadOnlyList<RagRetrievedItem>` |
| `ToolSupplierHit` | `supplierCode, supplierName` | `SupplierSummary` on the model path |
| `ToolSupplierList` | `count, suppliers[]` | bare `IReadOnlyList<SupplierSummary>` |
| `ToolSkillActivation` | `name, found, body, message` | prose string |
| `ToolRequisitionWrite` | `success, requisitionId, message` | prose string |

`MarshalResult` is removed from `RegisterFunction` so `AIFunctionFactory`'s default JSON marshalling applies.

Rationale: explicit `[JsonPropertyName]` is the belt-and-braces choice — it wins regardless of whether the serializer applies a camelCase naming policy. Wrapping arrays in an object with a `count` is the single highest-value part: it lets the model distinguish "no matches" from a broken response, which the bare array could not. Turning prose into `found`/`success` flags moves the branch out of natural-language parsing.

**Deliberate coupling rule:** handlers currently do `_turnContext.RetrievedItems.AddRange(ragItems)` and return the same list, so the report and the model share one object. The new types are mapped separately from the same domain data; the report keeps `RagRetrievedItem` and its PascalCase JSON. This is what lets the report contract stay frozen while the model-facing payload changes. Cost: one extra mapping call per search.

### D5: Preserve the message strings verbatim

The prose in the new `message` fields is copied character-for-character from the current returns — `"Requisition created: {id}."`, `"Missing or invalid required fields..."`, `"...is not registered for that item..."`, `"Unknown skill '{name}'. Available skills: {names}."`. `create_requisition`'s success message keeps the `"Requisition created: <id>."` prefix even though `requisitionId` is now a first-class field, so the report-adjacent prose assertions in `SkillFrameworkTests` and `ItemSupplierCombinationTests` keep passing.

Rationale: minimizes the blast radius. The redundant prefix is mildly inelegant in exchange for four test suites not needing rewrite for a cosmetic reason. `SkillFrameworkTests.ExtractRequisitionId` *is* updated to read the `requisitionId` field — scraping an id out of prose was the fragile part.

### D6: `ILogger<PurchaseRequisitionTools>` over console tracing

Inject `ILogger<PurchaseRequisitionTools>`. Each handler logs at Debug/Information: tool wire name, the argument names supplied, elapsed milliseconds, and the result count. `RecordToolCall` is unchanged — it is not tracing, it is report bookkeeping.

Rationale: the reference pattern's `Console.WriteLine` is a console app affordance; this is a hosted ASP.NET service that already has `ILogger` wired, and the existing `ChatService` logs through it. Structured fields survive container log aggregation.

### D7: Schema regression test as a first-class artifact

New `ToolSchemaTests` walks every tool's `AIFunction.JsonSchema` and asserts each non-cancellation-token parameter has a non-empty `description`, and that `search_by_codes` has an empty `required` array.

Rationale: the bug being fixed is *invisible* to every other test — nothing fails when parameter descriptions vanish; the model just gets slightly worse at filling arguments. A schema assertion is the only test that would have caught it. Without this, D1 can silently regress.

## Risks / Trade-offs

- **Schema test asserts on a `JsonElement` shape** → if `AIFunctionFactory`'s schema representation changes across M.E.AI versions, the test fails for the wrong reason. Mitigation: the assertions read named JSON properties, not serialized text, so a representation change surfaces as a clear failure.
- **Removing the passthrough marshalling could change what the model receives** → the passthrough was a deliberate choice recorded in the archived `2026-09-01-agentic-tool-chat` design. If the OpenAI adapter stringifies a non-`string`, non-`JsonElement` `Result` via `.ToString()`, the model would see type names — a latent bug the passthrough happens to avoid. Mitigation: `ToolSchemaTests` gains a marshalling assertion that a tool result arrives as JSON text containing the expected camelCase keys, proving the boundary is intact before and after the change. The build is run in the devcontainer; the tests need Postgres, as all agentic tests do.
- **Result objects cost more tokens than bare arrays** → wrapping adds a few tokens per call on the retrieval path. Accepted: the `count` field and the removal of `Similarity: null` noise are worth it for the reliability gain on every search.
- **`[Description]` on result properties is not transmitted to the model** → OpenAI's function contract carries parameter schemas only; there is no return schema. Those attributes document intent for C# readers and any future adapter that does surface them, and they are not a claimed model-facing win. The model-facing levers are the camelCase keys, the `count`, and the boolean flags.
- **Two object shapes now exist for a retrieved requisition** → `RagRetrievedItem` and `ToolRequisitionHit` cover the same fields. Mitigated by mapping both from the same domain object in the handler and by a comment stating the report DTO is frozen. Accepted as the price of an unfrozen model-facing contract.
- **Model behavior may shift after the result-shape change** → prose outcomes become flags, so a model that previously pattern-matched `"Requisition created:"` in prose now reads `success`. This should be neutral-to-better, but it is a live-model change that only a demo with a real key can confirm. Mitigation: task to re-run the compose `demo` profile and walk the create-requisition flow.
- **Spec drift already exists in the repo** → `agent-framework-layering` and `skill-framework` both said "four tools" after the fifth landed, and `AGENTS.md` still documents a `[Skill: name]` marker and a `FakeQueryRewriter` that no longer exist. This change fixes the tool-count text it touches; the `AGENTS.md` items are corrected in the same change since they are actively misleading. Remaining spec drift is out of scope.

## Migration Plan

- No schema migration, no data migration, no DI change. `ILogger<T>` resolves without registration.
- Deploy order is irrelevant — one commit, one build. Existing skill markdown and prompt text need no edit because wire names are unchanged.
- Rollback: revert the commit. Nothing persisted by a tool invocation is affected; the report files written during a session with the new shape are the same as before.

## Open Questions

- None blocking.
- Open follow-up (not this change): tool invocations are still invisible to the client — neither the chat response nor the SSE stream reports them. Surfacing tool activity in the API/UI is a separate capability.
