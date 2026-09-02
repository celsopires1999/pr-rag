## 1. Repository tool surface

- [x] 1.1 Add tool-facing retrieval methods to `IPurchaseRequisitionRepository` / `PurchaseRequisitionRepository` that return structured results usable by tool delegates (e.g. `SearchByCodesAsync` and `SearchAsync` already exist — expose any shaping/adapter needed for AIFunction delegates)
- [x] 1.2 Confirm `SearchByCodesAsync` handles the `ITM-*` / `SUP*` code input used by the exact-match tool
- [x] 1.3 Confirm `SearchAsync` takes `(float[] embedding, int topK, double minSimilarity)` and returns `RequisitionSearchResult`

## 2. Chat client tool wiring

- [x] 2.1 Build the two AIFunctions (exact-match code lookup `search_by_codes`, semantic search `search_semantic`) in `ChatService` via `AIFunctionFactory`, bound to `IPurchaseRequisitionRepository` and `IEmbeddingService` (both Application abstractions), keeping the loop in Application (no infra deps)
- [x] 2.2 Drive tool selection/manual invocation loop in Application instead of a `FunctionInvokingChatClient` DI wrapper, respecting the Application->Infrastructure dependency direction and scoped-service access
- [x] 2.3 Keep `top_k` / `min_similarity` flowing into the semantic-search tool delegate from request or `RagSettings`
- [x] 2.4 `IQueryRewriter` left wired in Infrastructure (unused by chat; retained to avoid regressions) — noted for follow-up removal

## 3. ChatService rework (Application)

- [x] 3.1 Remove the regex-based `ITM-*`/`SUP*` dispatch and forced `RetrieveContextAsync` pipeline from `ChatService`
- [x] 3.2 Forward the full `request.Messages` history + current `Question` as the conversation to the tool-calling chat client
- [x] 3.3 Compute `retrieved_count` / answer from tool results returned in the final turn for `AnswerAsync`
- [x] 3.4 Route `StreamAsync` through the same tool-calling client, streaming assistant tokens and signaling completion
- [x] 3.5 Preserve the `POST /api/chat` and `/api/chat/stream` response shapes and `NoContext`-style graceful answer when no usable tool context
- [x] 3.6 Keep `RagQueryReport` observability emission (rewritten-query fields nullable/removed as needed)

## 4. Tests

- [x] 4.1 Add/extend a fake chat client that simulates tool-call selection and result return
- [x] 4.2 Integration test: model calls exact-match tool and answer is grounded on matched requisitions
- [x] 4.3 Integration test: model calls semantic-search tool and answer is grounded above the similarity threshold
- [x] 4.4 Integration test: model answers without retrieval (continuous conversation, no tool)
- [x] 4.5 Integration test: full history carried across turns so a follow-up references earlier tool results

## 5. Front-end verification (web/)

- [x] 5.1 Verify `web/` sends the full rolling message history in `/api/chat/stream` requests for continuity
- [x] 5.2 Build `web/` (`npm install && npm run build`) to confirm no regressions

## 6. Verify

- [x] 6.1 Run `dotnet build PrRag.sln`
- [x] 6.2 Run integration tests with `TEST_CONNECTION_STRING` against a reachable Postgres
