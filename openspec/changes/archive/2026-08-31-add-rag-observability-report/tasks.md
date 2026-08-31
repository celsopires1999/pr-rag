## 1. Report Data Model

- [x] 1.1 Add `RagQueryReport` DTO (with `Timestamp`, `Question`, `TopK`, `MinSimilarity`, `TopKFromRequest`, `MinSimilarityFromRequest`, `RewrittenQuery`, `RetrievedItems`, `Answer`, `RetrievedCount`, `UsedNoContextFallback`) to `src/PrRag.Application/DTOs/`
- [x] 1.2 Add nested `RagRetrievedItem` DTO (id, supplier/item fields, and similarity score where available) to `src/PrRag.Application/DTOs/`
- [x] 1.3 Add `IRagReportWriter` abstraction to `src/PrRag.Application/Abstractions/` with a `WriteAsync(RagQueryReport)` method

## 2. Surface Similarity Scores from Retrieval

- [x] 2.1 Extend `SearchAsync` in `IPurchaseRequisitionRepository` (and its implementation in `src/PrRag.Infrastructure/Services/PurchaseRequisitionRepository.cs`) to also return each retrieved requisition's similarity score (e.g. project `1 - (embedding <=> {0})` alongside the row)
- [x] 2.2 If a distinct result shape is needed, introduce a small result record (requisition + score) and update callers accordingly (check `ChatService` and tests before changing signatures)

## 3. Integrate Report Emission into the Pipeline

- [x] 3.1 Inject `IRagReportWriter` into `ChatService` constructor (`src/PrRag.Application/Services/ChatService.cs`)
- [x] 3.2 Capture the effective `top_k`/`min_similarity` and whether each came from the request or defaults inside `AnswerAsync` (lines 46-47)
- [x] 3.3 Record the rewritten query and the retrieved requisitions (code-search and vector-search results with similarity scores) during the pipeline
- [x] 3.4 Capture the final answer (and the no-context fallback case, `RetrievedCount = 0`), then build and write a `RagQueryReport` at the end of `AnswerAsync`
- [x] 3.5 Wrap the report write so an exception in the writer is caught and logged (via `ILogger<ChatService>`) without affecting the returned response

## 4. Report Writer Implementation & Configuration

- [x] 4.1 Implement `FileRagReportWriter` in `PrRag.Infrastructure` that serializes `RagQueryReport` with `System.Text.Json` (indented) to a file named with a unique timestamp + short id suffix
- [x] 4.2 Create the output directory on first use (default from configuration `RAG__Report__OutputDirectory`, falling back to `./reports`)
- [x] 4.3 Register `IRagReportWriter` → `FileRagReportWriter` (scoped or singleton) and bind the output directory setting in `src/PrRag.Infrastructure/DependencyInjection.cs`
- [x] 4.4 Add the default `RAG__Report__OutputDirectory` value to `.env.example` (and `Configuration` settings class if one is introduced)

## 5. Ensure Reports Are Not Committed

- [x] 5.1 Add the report output directory (e.g. `reports/`) to `.gitignore`
- [x] 5.2 Verify with `git status` that no generated report files are tracked after a run

## 6. Tests & Verification

- [x] 6.1 Add/update tests (e.g. in `tests/PrRag.Tests/`) verifying a report file is written with the question, effective `top_k`/`min_similarity`, and answer after calling the chat pipeline
- [x] 6.2 Verify the no-context fallback path still writes a report with empty retrieval and the fallback answer
- [x] 6.3 Confirm `POST /api/chat` response body is unchanged (response contract untouched) and run `dotnet test`
