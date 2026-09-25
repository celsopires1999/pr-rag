## Why

The agent's five tools advertise parameter-level documentation that never reaches the model. `PurchaseRequisitionTools` registers each tool with a lambda that forwards to a `[Description]`-annotated method, so `AIFunctionFactory` builds the JSON schema from the *lambda's* parameters — which carry no attributes. Every `[Description("The item codes to search for")]` on a parameter is silently dropped at the wire. Alongside that, tool descriptions are written twice (a `private const string` *and* a `[Description]` attribute), wire names are hardcoded twice per tool (registration + `RecordToolCall`), and tool results are passed through to the model as raw CLR objects with PascalCase properties and no result count, so the model cannot tell an empty result from a malformed one.

## What Changes

- Register tools from their `[Description]`-annotated method groups instead of forwarding lambdas, so every parameter description reaches the generated JSON schema. `search_by_codes`'s `items`/`suppliers` stay optional.
- Remove the duplicated `*Description` constants; the `[Description]` attribute on each handler becomes the single source of the tool description, and the handler method's own description is no longer overridden at registration.
- Introduce a public const per tool wire name, used by both registration and `RecordToolCall`, eliminating the duplicated string literals that could silently drift into the observability report.
- **BREAKING (model-facing)**: tools return typed, camelCase result DTOs (`ToolSearchResult`, `ToolSupplierList`, `ToolSkillActivation`, `ToolRequisitionWrite`) serialized as JSON, replacing the passthrough of raw domain/DTO objects. `activate_skill` and `create_requisition` no longer return bare strings; they return objects carrying the same human-readable message text in a `message` field.
- Replace ad-hoc `Console.WriteLine`-style tracing with `ILogger<PurchaseRequisitionTools>` structured logging (tool name, elapsed ms, result count). The existing `RecordToolCall` bookkeeping that feeds the RAG observability report is unchanged.
- Add a schema regression test asserting every tool parameter carries a non-empty description.

## Capabilities

### New Capabilities

None. This change hardens the existing tool contract rather than adding agent capability.

### Modified Capabilities

- `agent-framework-layering`: the tools-class requirement currently fixes the list at four tools and says each `[Description]` is "used verbatim in tool registration" — it changes to the five-tool set, and adds the requirements that tool metadata is declared exactly once and that parameter descriptions reach the generated schema.
- `maf-agent-integration`: adds a requirement that tool results are typed, camelCase, JSON-marshalled DTOs rather than passthrough CLR objects.
- `skill-framework`: `activate_skill` now returns a structured activation result (found flag + body + message) instead of a bare string, and the stale "fixed framework set" listing of four tools becomes five.
- `suppliers-by-item`: `get_suppliers_by_item` returns a counted supplier list object rather than a bare array.
- `purchase-requisition-creation-guard`: `create_requisition` returns a structured write result carrying an explicit `success` flag instead of a prose-only outcome.

## Impact

- `PrRag.Application`:
  - `PurchaseRequisitionTools` — method-group registration, centralized tool-name consts, `ILogger` injection, typed return values, passthrough marshalling removed.
  - New `Services/Agents/ToolResults.cs` — the tool result DTOs and their mappers.
  - `AgentInstructions.CoreInstructions` — unchanged text; the `<ALLOWED_ACTIONS>` bullets already name the five tools, which stays accurate.
- `PrRag.Tests`:
  - New `ToolSchemaTests` (schema regression) and a marshalling assertion for the JSON result shape.
  - `AgentFrameworkLayeringTests` — tool-name assertions move to the shared consts; adds a "every tool has a description" check.
  - `GetSuppliersByItemTests` — asserts the counted supplier object shape.
  - `SkillFrameworkTests` — `ExtractRequisitionId` reads the `requisitionId` field instead of scraping prose; the four prose `Assert.Contains` checks continue to pass because the message text is preserved verbatim.
- Observability report contract (`RagQueryReport`, `RagRetrievedItem`, `RagToolCall`) is **unchanged** — the tool result DTOs are deliberately decoupled from the report schema.
- No database schema, migration, DI-registration, or frontend changes.
