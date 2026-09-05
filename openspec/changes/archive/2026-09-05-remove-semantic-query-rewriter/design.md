## Context

Chat is served by `ChatService` (agentic retrieval): the chat model decides whether to call tools `search_by_codes` (exact code lookup) and `search_semantic` (vector similarity). The `ChatService.SystemPrompt` already instructs the model to rewrite the user's question into a short, keyword-rich query and use the full conversation history to disambiguate references before calling `search_semantic`.

Currently `SearchSemanticAsync` still runs a redundant second rewrite: it takes the model's `query` argument and passes it through `IQueryRewriter.RewriteAsync`, then embeds the rewriter's output. This was a leftover from the pre-agentic pipeline, retained "to avoid regressions" when agentic retrieval landed. The rewriter doubles the LLM round-trips per semantic query.

The rewriter's only consumers are `ChatService` and tests (`FakeQueryRewriter`, `AgenticRetrievalTests`, `RagObservabilityReportTests`). No HTTP-facing contract depends on it.

## Goals / Non-Goals

**Goals:**
- Remove `IQueryRewriter`, `SemanticQueryRewriter`, and the DI registration, so `search_semantic` embeds the model-generated query directly.
- Keep the RAG observability report contract, recording the query actually used for vector search.
- Remove test fakes/assertions tied to the rewriter and keep the suite green.

**Non-Goals:**
- Changing the `search_semantic` tool signature, embedding model, or retrieval parameters.
- Rewording the chat system prompt's rewrite instructions (already correct).
- Renaming/redesigning report fields beyond dropping rewriter-specific semantics.

## Decisions

**D1. Embed the model's query directly in `SearchSemanticAsync`.**
The tool handler receives the already-optimized query from the model; embed it as-is. Removes one LLM call per semantic search (latency + cost).
```csharp
private async Task<IReadOnlyList<RagRetrievedItem>> SearchSemanticAsync(
    string query,
    CancellationToken cancellationToken)
{
    var embedding = await _embeddingService.GenerateAsync(query, cancellationToken);
    var results = await _repository.SearchAsync(embedding, _activeTopK, _activeMinSimilarity, cancellationToken);
    return results.Select(r => RagRetrievedItem.From(r.Requisition, r.Similarity)).ToList();
}
```
*Alternative considered:* keep the rewriter but register a fake in production — rejected, it just hides dead code.

**D2. Keep `_activeRewrittenQuery` / report `RewrittenQuery`, populated with the model's query.**
The observability requirement ("the rewritten query when vector search is performed") still holds — the model-generated query is the query used for search. This avoids a report-contract change and keeps the RAG report useful for debugging. `_activeRewrittenQuery` is set in `SearchSemanticAsync` from the model's `query` argument; it remains null when only `search_by_codes` is used or no retrieval runs, matching today's behavior.
*Alternative considered:* remove `RewrittenQuery` entirely and update the `rag-observability-report` spec — rejected to keep the change scoped to the rewriter removal.

**D3. Delete rewriter artifacts wholesale.**
Remove `IQueryRewriter`, `SemanticQueryRewriter`, `FakeQueryRewriter`, the `AddScoped` registration, and the two `AgenticRetrievalTests` cases that assert rewriter behavior (`Semantic_search_rewrites_query_with_full_conversation`, `Exact_match_tool_does_not_invoke_query_rewriter`). Delete `FakeQueryRewriter` wiring from `IntegrationServiceFactory`.
*Alternative considered:* keep `IQueryRewriter` for future use — rejected; YAGNI, and the archived change explicitly flagged removal as follow-up.

**D4. Update `RagObservabilityReportTests` assertion.**
`Report_written_with_question_parameters_and_answer` asserts `report.RewrittenQuery == "optimized: acme hydraulic pump"` (fake rewriter prefix). After removal it becomes `"acme hydraulic pump"` (the model's query). The no-context fallback test is unaffected (no vector search → `RewrittenQuery` null).

## Risks / Trade-offs

- [Dropped rewriter means a potentially less-optimized query if the model ignores the system-prompt rewrite instruction] → The model is explicitly instructed per turn; the system prompt and examples remain in place. Latency/cost savings justify one rewrite step.
- [`RewrittenQuery` field name now slightly misrepresents "rewritten" (it is the model's own query)] → Acceptable; observable behavior of the report (capture query used for vector search) is preserved; naming change deferred to a dedicated report-refactor change if desired.
- [Stale references to `IQueryRewriter`/`SemanticQueryRewriter` left behind] → `dotnet build` + test run catch dangling references; grep the workspace for `IQueryRewriter`/`SemanticQueryRewriter`/`FakeQueryRewriter` before finishing.

## Migration Plan

Deploy with the rest of the API service — no schema or HTTP changes. Rollback is a revert of the PR (restores the rewriter and its DI registration).

## Open Questions

None. The archived `agentic-tool-chat` design already anticipated this removal.