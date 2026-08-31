## 1. Query Rewriter abstraction

- [x] 1.1 Add `IQueryRewriter` interface in `src/PrRag.Application/Abstractions/` with `Task<string> RewriteAsync(string question, CancellationToken ct = default)`

## 2. Query Rewriter implementation

- [x] 2.1 Add `OpenAiQueryRewriter` in `src/PrRag.Infrastructure/Services/` implementing `IQueryRewriter` using the registered `IChatClient`
- [x] 2.2 Define the system prompt constant (remove noise, translate to English, focus on entity/field values, return only the query) with few-shot Portuguese→English examples
- [x] 2.3 Register `IQueryRewriter` in `src/PrRag.Application/DependencyInjection.cs` (via interface) and implementation wiring

## 3. ChatService integration

- [x] 3.1 Inject `IQueryRewriter` into `ChatService`
- [x] 3.2 In `AnswerAsync`, inside the vector-search branch (`results.Count < topK`), call `RewriteAsync(request.Question)` and use the returned query for embedding instead of `request.Question`
- [x] 3.3 Ensure `BuildPrompt` still uses the original `request.Question` for the final answer
- [x] 3.4 Confirm rewriter propagates exceptions (no fallback to raw question)

## 4. Verification

- [x] 4.1 Add/adjust xUnit tests covering: rewriter runs only when vector search is needed, and original question used in final prompt
- [x] 4.2 Build and run tests (`dotnet build` + `dotnet test`) — all 6 tests pass
