## Why

The RAG observability report currently records what the retrieval produced (rewritten query, retrieved items, skill state) but not which tools the agent called to produce the answer. This makes it impossible to tell, from a report alone, whether `search_semantic`, `search_by_codes`, `activate_skill`, or `create_requisition` were used — a key piece of the question-to-answer trace.

## What Changes

- Add a `ToolCalls` list to the `RagQueryReport` DTO, populated from the agent turn.
  - Each entry records the tool name and the arguments it was invoked with, in call order.
- Track tool invocations during a turn:
  - `AgentTurnContext` gains a per-turn list of tool call records, cleared at `Begin`.
  - Each handler in `PurchaseRequisitionTools` records its own invocation (name + arguments).
- `ChatService.WriteReportAsync` copies the recorded invocations into the report.
- The report writer (`FileRagReportWriter`) needs no change — it serializes the DTO as-is.

## Capabilities

### New Capabilities
_None._

### Modified Capabilities
- `rag-observability-report`: the per-request report now also records the tools (and their arguments) invoked to produce the answer.

## Impact

- `PrRag.Application`:
  - `DTOs/RagReportDtos.cs` — new `RagToolCall` DTO plus `ToolCalls` field on `RagQueryReport`.
  - `Services/Agents/AgentTurnContext.cs` — per-turn tool invocation tracking.
  - `Services/Agents/PurchaseRequisitionTools.cs` — each handler records its invocation.
  - `Services/ChatService.cs` — populate `ToolCalls` in `WriteReportAsync`.
- `PrRag.Tests`:
  - New/updated assertions in `RagObservabilityReportTests` verifying tool call records (non-streaming, streaming, no-context fallback).
- Report JSON output gains a `ToolCalls` array. Existing fields are unchanged, so old consumers still parse.