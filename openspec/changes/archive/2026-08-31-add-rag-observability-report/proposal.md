## Why

Today there is no way to observe how the RAG application actually behaves for a given question: which parameters (`top_k`, `min_similarity`) were used, what the rewriter produced, what was retrieved, and what answer was returned. Without this record, tuning retrieval behavior is guesswork. We need a non-committed report that captures the full question-to-answer trace so we can observe behavior and adjust parameters when needed.

## What Changes

- Introduce a per-request observability report that captures the full RAG pipeline trace, written to a local, non-committed report generated at request time.
- The report SHALL record: the timestamp, the original question, the effective `top_k` and `min_similarity` used (whether from the request or the configured defaults), the rewritten query (when vector search is used), the retrieved requisitions with their similarity scores, and the final answer returned to the user.
- The report SHALL be generated as a machine-readable file (JSON) so it can be inspected and diffed across runs to tune retrieval.
- Report files SHALL be written to a dedicated output directory that is excluded from git (non-committed), while keeping the code that produces them tracked.

## Capabilities

### New Capabilities
- `rag-observability-report`: capturing a per-request question-to-answer trace (question, effective `top_k`/`min_similarity`, rewritten query, retrieved context with similarity scores, final answer) into a non-committed local report.

### Modified Capabilities
- `chat-query`: the RAG answer pipeline SHALL additionally emit the observability record for each answered question (no change to the answer semantics or response body).

## Impact

- **Code**: `PrRag.Application/Services/ChatService.cs` (emit trace), `PrRag.Application/DTOs/` (new report DTO), `PrRag.Application` abstractions, and API host if report output path is configured.
- **Configuration**: new setting for the report output directory (default local, e.g. `./reports`); no secrets involved.
- **Dependencies**: none new (uses existing `System.Text.Json`).
- **Repo hygiene**: report directory added to `.gitignore`; generated reports never committed.
- **README**: document how to generate and inspect reports (optional).
