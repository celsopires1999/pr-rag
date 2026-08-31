## Why

User questions currently go directly to vector search, embedding the raw question with all its noise (slang, pleasantries, redundancy) and in whatever language the user speaks. But the retrieval index is built from English text. This degrades retrieval quality. We need a step that rewrites the user's question into an optimized, noise-free, English query before embedding.

## What Changes

- Add a query rewriter step that, via an LLM call, transforms the raw user question into a short, keyword-rich, English query optimized for cosine similarity search against the indexed fields (Supplier Name, Item Name, Description, codes).
- The rewriter runs only when vector search is needed — i.e., when the exact code-based search returned fewer than `top_k` results.
- If the rewriter fails (timeout, API error), the request fails rather than falling back to the raw question.
- The rewritten query is used only for embedding/retrieval; the original question is preserved and used in the final answer prompt so the LLM responds naturally.
- The vector search complements the code search up to `top_k` total (existing merge behavior).

## Capabilities

### New Capabilities
- `query-rewriter`: Rewrites a raw user question into an optimized, English, keyword-rich query for semantic retrieval.

### Modified Capabilities
- `chat-query`: The retrieval path now conditionally rewrites the question via the query rewriter before embedding, only when vector search is required.

## Impact

- `src/PrRag.Application/Services/ChatService.cs`: Insert rewriter call in the vector-search branch, use rewritten query for embedding.
- `src/PrRag.Application/Abstractions/`: New `IQueryRewriter` interface.
- `src/PrRag.Infrastructure/Services/`: New `OpenAiQueryRewriter` implementation using `IChatClient`.
- `src/PrRag.Application/DependencyInjection.cs` and `src/PrRag.Infrastructure/DependencyInjection.cs`: Wire the new service.
- Config: possibly a new prompt constant; no new settings.
- Tests: cover rewriter and rewriter-driven retrieval behavior.
