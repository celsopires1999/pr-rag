## REMOVED Requirements

### Requirement: Rewrite question for semantic retrieval
**Reason**: The chat model now rewrites the user question into the optimized semantic search query inline as part of its agentic reasoning loop (see `agentic-retrieval`). The separate rewriter performed a redundant second LLM rewrite that was no longer used.
**Migration**: `ChatService.SearchSemanticAsync` embeds the query argument produced by the chat model directly; no rewriter is invoked. The chat model's rewriting behavior is governed by the `agentic-retrieval` capability.

### Requirement: Rewrite using full conversation context
**Reason**: Conversation-context disambiguation for semantic queries is now performed by the chat model itself, which already receives the full conversation history. A dedicated rewriter component is no longer needed.
**Migration**: The system prompt instructs the model to use the full conversation history to resolve references before calling `search_semantic`. See `ChatService.SystemPrompt`.

### Requirement: Rewriter failure surfaces as error
**Reason**: With the rewriter removed, there is no separate LLM call to fail; the semantic search tool uses the query the model already produced.
**Migration**: No fallback behavior exists — the model's query is embedded as-is and any failure occurs on the embedding or vector search step, which surfaces as a request error as before.

### Requirement: Rewriter implementation lives in the Application layer
**Reason**: The rewriter implementation no longer exists.
**Migration**: Remove `IQueryRewriter`, `SemanticQueryRewriter`, and the `AddScoped<IQueryRewriter, SemanticQueryRewriter>()` registration. Semantic query generation is now part of `ChatService`'s system-prompt-driven model behavior.