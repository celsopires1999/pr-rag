## Context

The RAG pipeline (`ChatService.AnswerAsync` in `PrRag.Application/Services/ChatService.cs`) answers a question through a sequence: resolve effective `top_k`/`min_similarity` (request vs configured defaults), optional code-based search, optional query rewrite + embedding + vector search, and a final OpenAI chat call built from the original question and retrieved context. Today the only visibility into this pipeline is ordinary `ILogger` output and `Microsoft.Extensions.AI` logging hooks, which do not produce a cohesive, inspectable per-request record. The user wants a local, non-committed report capturing the question, `top_k`, `min_similarity`, and everything needed up to the answer, so behavior can be observed and tuned.

Constraints:
- Report files must never be committed (added to `.gitignore`).
- The change must not alter answer semantics or the `POST /api/chat` response body.
- No new external dependencies; the codebase already references `System.Text.Json`.

## Goals / Non-Goals

**Goals:**
- Capture a per-question trace spanning the whole pipeline: timestamp, original question, effective `top_k` and `min_similarity` (and whether each came from the request or defaults), rewritten query, retrieved requisitions with similarity scores and which are actually used, and the final answer sent to the user.
- Persist each trace as an inspectable JSON file in a local, non-committed directory.
- Keep the producing code under version control while excluding generated report files.
- Support tuning by letting a developer compare reports across parameter/retrieval changes.

**Non-Goals:**
- No UI/dashboard, no metrics collection, no OpenTelemetry/tracing integration.
- No change to the RAG answer or the public `POST /api/chat` response contract.
- No streaming or request logging middleware for arbitrary endpoints (chat-only).
- No retaining the trace in a database (ephemeral local files).

## Decisions

### 1. Emit a trace inside `ChatService.AnswerAsync` rather than via middleware
Capture the trace at the single point where the full pipeline already runs, piping the trace object through the existing flow and writing it once after the answer is produced. A `IRagReportWriter` abstraction is injected so the writer can be swapped or disabled without touching service logic.
- **Alternative considered**: request middleware + intercepting repo calls — too invasive, loses cohesive ordering, and can't easily associate rewriter/embedding output with the final answer.

### 2. JSON file per request for full fidelity
Each request writes one JSON file named with a timestamp (and a short id/suffix to guarantee uniqueness under concurrent requests). JSON preserves structure (arrays, similarity scores, retrieved requisitions) and diffs cleanly across runs for tuning.
- **Alternative considered**: single rolling CSV — loses nested structure (retrieved list, per-item scores) and mixes concurrent runs.

### 3. Configurable output directory, excluded from git
Add an `IRagReportWriter` whose output directory comes from configuration (`RAG__Report__OutputDirectory`), defaulting to `./reports`. Report files are written there and the directory is added to `.gitignore`. The writer creates the directory on first use and no-ops (or skips writing) if nothing is captured.
- **Alternative considered**: writing under `data/` — that directory is for input data and `*.json` is already ignored globally, but a dedicated `reports/` dir keeps reports separate and avoids ambiguity with the ingested dataset.

### 4. Keep trace data model separate from response DTOs
Define `RagQueryReport` (and nested `RetrievedItem`) as a new DTO in `PrRag.Application/DTOs/`, independent of the public `ChatRequest`/`ChatResponse`. The public contract stays untouched; the report is an internal observability artifact.
- **Alternative considered**: reusing `ChatRequest`/`ChatResponse` — couples public contract to internal observability and would leak fields into the API shape.

### 5. Raw retrieved results for the report while the answer uses filtered ones
Capture the retrieved requisitions as returned by the repository (including code-search and vector-search results and their similarity scores) so behavior can be diagnosed, while the existing answer-building logic continues to use exactly what it uses today. The writer is responsible only for serialization; no change to which items feed the prompt.

## Risks / Trade-offs

- [Disk growth from one file per request] → Mitigation: configurable output directory; reports are ephemeral local files; can be cleaned by the developer; not written into git.
- [Sensitive purchase data in report files] → Mitigation: files are local and git-ignored; document that the directory must not be committed; no API key or secrets are captured.
- [Concurrent writes colliding on file name] → Mitigation: unique per-request suffix (timestamp + short id) in the file name.
- [Nil/empty retrievals producing sparse reports] → Mitigation: writer still emits the trace with empty retrieved lists and the final fallback/answer so tuning context is preserved.
- [Writer failure should not break the answer] → Mitigation: writer is invoked for its side effect only; any exception is caught and logged without affecting the response.
