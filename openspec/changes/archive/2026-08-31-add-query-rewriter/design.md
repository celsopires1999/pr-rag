## Context

`ChatService.AnswerAsync` currently embeds the raw user question directly (`ChatService.cs:73`). The retrieval index (`PurchaseRequisition.EmbeddingSource`) is composed in English from fields: Supplier Code, Supplier Name, Item, Item Name, Description. Users ask in Portuguese (and other languages) with slang and filler, which pollutes the embedding and lowers cosine similarity against the English index.

We introduce a query-rewriting step that, when vector search is required, rewrites the raw question into a short, keyword-rich English query before embedding.

## Goals / Non-Goals

**Goals:**
- Improve retrieval quality by embedding a clean, English, domain-focused query instead of the raw question.
- Only pay the extra LLM call when vector search is actually needed.
- Keep the original question for the final answer so the LLM still responds naturally.

**Non-Goals:**
- Detecting intent, temporal filters, or decomposition (level 3/4 preprocessing).
- Caching rewrite results.
- Changing the merge/ordering semantics of code vs vector results (Opção 2 preserved).
- Changing the code-regex extraction step.

## Decisions

### 1. New `IQueryRewriter` abstraction
A small interface in `PrRag.Application/Abstractions`:

```
IQueryRewriter
    Task<string> RewriteAsync(string question, CancellationToken ct = default);
```

Returns only the optimized query string. Rationale: mirrors existing `IEmbeddingService`/`IChatClient` style; keeps the Application layer decoupled from any specific LLM provider.

### 2. `OpenAiQueryRewriter` implementation
In Infrastructure, implements `IQueryRewriter` using the already-registered `IChatClient` (GPT-4o-mini default). A constant system prompt instructs the model to:
- Remove slang, pleasantries, filler words.
- Translate to English when the question is in another language (the index is English).
- Focus on entity names, concepts, and field values.
- NOT emit field labels like `supplier_code:` — only values.
- Return ONLY the optimized query, nothing else.

The prompt includes the indexed fields and 3-4 in/out examples (Portuguese → English).

### 3. Conditional execution in `ChatService`
The rewriter is invoked only inside the existing vector-search branch, i.e. when `results.Count < topK`. The rewritten query replaces `request.Question` as the embedding input:

```
if (results.Count < topK)
{
    var optimizedQuery = await _queryRewriter.RewriteAsync(request.Question, ct);
    var embedding = await _embeddingService.GenerateAsync(optimizedQuery, ct);
    var vectorResults = await _repository.SearchAsync(embedding, topK, minSimilarity, ct);
    // merge per Opção 2 (preenche até topK)
}
```

The `BuildPrompt` still uses `request.Question` (original) for the final answer.

### 4. Failure semantics: fail, don't fall back
If `RewriteAsync` throws (timeout, API error), the exception propagates and the request fails. We deliberately do NOT fall back to the raw question. Rationale: consistent with the user's decision, and a silently degraded embedding would produce worse retrieval without signaling the problem.

### 5. No cache
Rewrite results are not cached. Rationale: per user, overkill at this stage; model non-determinism is acceptable since small variations still yield similar vectors.

## Risks / Trade-offs

- **Extra latency/cost** → One additional GPT-4o-mini call per vector search. Mitigation: only runs when vector search is needed; model is cheap and fast.
- **Rewriter non-determinism** → Same question may yield slightly different queries. Impact is minimal for cosine similarity search.
- **Rewriter produces a query that drifts off-field** → Mitigation via prompt constraints (field list, "values only") and few-shot examples.
- **Rewriter failure blocks request** → Accepted trade-off per user decision; surfaces errors explicitly.
