## Context

The RAG observability report (`RagQueryReport` DTO, written as JSON by `FileRagReportWriter`) already captures the retrieval side of each turn: rewritten query, retrieved items, skill activation, and the final answer. What it does not capture is which agent tools were invoked. The agentic loop runs inside the Microsoft Agent Framework `ChatClientAgent`, so tool execution is the only reliable intercept point — but each of the four tool handlers (`PurchaseRequisitionTools`) already flows through the scoped `AgentTurnContext`, which is the natural place to record invocations.

The report is written from `ChatService.WriteReportAsync` (both `AnswerAsync` and `StreamAsync` paths), which already reads turn-scoped state from `AgentTurnContext`, so no change to `FileRagReportWriter` is needed — it serializes the DTO as-is.

## Goals / Non-Goals

**Goals:**
- Record which tools were invoked during a turn in the report, with their arguments, in call order.
- Do it for all four tools: `search_by_codes`, `search_semantic`, `activate_skill`, `create_requisition`.
- Cover both the non-streaming and streaming chat paths.
- Keep report JSON backward compatible (new field only, existing fields untouched).

**Non-Goals:**
- Recording tool *results* or return values (only usage + arguments).
- Exposing tool calls through the `ChatResponse` API DTO.
- Token/latency telemetry; only tool usage.

## Decisions

### Capture invocations at the tool-handler boundary
Each handler in `PurchaseRequisitionTools` records its own invocation into `AgentTurnContext` at the start of execution (before side effects), via a shared `RecordToolCall(name, args)` helper.
- **Why**: The MAF agent loop hides tool dispatch inside the framework; handlers are the only place with both the wire-level tool name and the concrete typed arguments. Recording at the top also captures calls that later fail or return an error string (e.g. unknown skill, refused requisition).
- **Alternative considered**: An `IChatClient` middleware wrapping `GetResponseAsync` to inspect `FunctionCallContent` in the message stream. Rejected — it intercepts model messages rather than actual executions, risks double counting (retries), and requires re-parsing arguments already available in typed form.

### New `RagToolCall` DTO; `RagQueryReport.ToolCalls` list
`RagToolCall { Name: string, Arguments: IReadOnlyDictionary<string, object?> }`. `RagQueryReport` gains `List<RagToolCall> ToolCalls`.
- **Why**: A dictionary keeps the DTO generic — each tool records its own argument shape (e.g. `{ query }`, `{ items, suppliers }`, `{ supplierCode, item, ... }`) without per-tool fields. Lists preserve call order. Call order within a single turn is inherent to the agent loop.
- **Alternative considered**: Separate per-tool boolean/count fields (e.g. `UsedSearchSemantic`). Rejected — loses arguments and order, and every new tool would require another field. Considered including a timestamp per call; rejected as noise since the report already carries a turn timestamp and the list is ordered.

### Track in `AgentTurnContext`, bind in `ChatService.WriteReportAsync`
`AgentTurnContext` gains a `List<RagToolCall> ToolCalls` cleared in `Begin(...)`. `ChatService` copies it into the report like it already does for `RewrittenQuery` and `RetrievedItems`.
- **Why**: `AgentTurnContext` is the existing per-turn bookkeeping channel between tools and the orchestrator (transient by design, never persisted). Both `AnswerAsync` and `StreamAsync` already funnel through the same `WriteReportAsync`, so both paths are covered with one change.

### Report remains backward compatible
`ToolCalls` is additive; `FileRagReportWriter` and the JSON schema are unchanged beyond the new property. Existing tests that deserialize reports keep passing; the no-context fallback path (no tools invoked) yields an empty list.

## Risks / Trade-offs

- **Arguments may contain sensitive data** (requisition details for `create_requisition`) → Reports already persist answer text and retrieved item descriptions; arguments are consistent in sensitivity. No extra personal data is introduced.
- **Tool invoked multiple times** (e.g. repeated `search_semantic`) → Recorded as repeated entries with each call's arguments; order preserved. This is desired observability, not a defect.
- **Serialization of argument values** (decimal quantities, string lists, nulls) → Handled by the default `System.Text.Json` serializer already used for reports; values are plain CLR types.
- **Future tool additions** → Each new handler must call `RecordToolCall` to appear in reports; a missed call silently yields an empty entry. Mitigated by a focused test that asserts handlers record their invocations.

## Migration Plan

No migration: report files are generated-per-request and additive. Older report readers ignore the new field; no config, DB, or API contract changes.

## Open Questions

None.