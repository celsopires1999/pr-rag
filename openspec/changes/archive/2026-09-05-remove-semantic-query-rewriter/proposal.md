## Why

The chat model already generates the semantic search query in the desired format: the `ChatService` system prompt instructs it to rewrite the user's question into a short, keyword-rich query before calling `search_semantic` (see `ChatService.cs` `SystemPrompt`). The separate `SemanticQueryRewriter` then rewrites the query a second time inside the tool handler — a redundant LLM call that adds latency and cost without changing the query. This removal was flagged as a follow-up when agentic retrieval landed (archived `2026-09-01-agentic-tool-chat` noted the rewriter was "unused by chat; retained to avoid regressions").

## What Changes

- **Remove the `IQueryRewriter` abstraction** and its `SemanticQueryRewriter` implementation, plus the `AddScoped<IQueryRewriter, SemanticQueryRewriter>()` DI registration.
- **`search_semantic` embeds the model-provided query directly**: `ChatService.SearchSemanticAsync` no longer calls a rewriter; the query argument produced by the chat model is embedded as-is.
- **RAG observability report** keeps `RewrittenQuery` but populates it with the model-generated query actually used for vector search, instead of the separate rewriter's output.
- **Remove `FakeQueryRewriter`** and its wiring from `IntegrationServiceFactory`, and delete the rewriter-specific tests in `AgenticRetrievalTests`.
- **BREAKING**: the public `IQueryRewriter` interface and `query-rewriter` spec capability are removed. No external consumers exist (tests are the only users).

## Capabilities

### New Capabilities
<!-- None. This change removes an existing capability. -->

### Modified Capabilities
- `agentic-retrieval`: The semantic search tool no longer invokes a separate query rewriter. The chat model performs the rewrite inline per the system prompt; scenarios referencing the rewriter component are updated.
- `query-rewriter`: All requirements removed — the capability is deleted entirely. Rewriting responsibility moves into the chat model's behavior described by `agentic-retrieval`.

## Impact

- **Code**: `src/PrRag.Application/Abstractions/IQueryRewriter.cs` and `src/PrRag.Application/Services/SemanticQueryRewriter.cs` deleted; `src/PrRag.Application/DependencyInjection.cs` loses the registration; `src/PrRag.Application/Services/ChatService.cs` drops `_queryRewriter` and the rewrite call in `SearchSemanticAsync`.
- **Tests**: `tests/PrRag.Tests/FakeQueryRewriter.cs` deleted; `IntegrationServiceFactory` drops the fake + registration; `AgenticRetrievalTests` loses `Semantic_search_rewrites_query_with_full_conversation` and `Exact_match_tool_does_not_invoke_query_rewriter`; `RagObservabilityReportTests` assertion updated (`RewrittenQuery` is now the model's query, not `optimized: <query>`).
- **API**: no public HTTP endpoint changes.
- **Specs**: `query-rewriter` capability removed; `agentic-retrieval` requirements updated.