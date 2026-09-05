## 1. Remove rewriter from Application layer

- [x] 1.1 Delete `src/PrRag.Application/Abstractions/IQueryRewriter.cs`
- [x] 1.2 Delete `src/PrRag.Application/Services/SemanticQueryRewriter.cs`
- [x] 1.3 Remove `services.AddScoped<IQueryRewriter, SemanticQueryRewriter>();` from `src/PrRag.Application/DependencyInjection.cs` (and the now-unused `using PrRag.Application.Abstractions;` if it becomes unused)

## 2. Update ChatService to embed the model's query directly

- [x] 2.1 Remove the `_queryRewriter` field, constructor parameter, and `IQueryRewriter` using from `src/PrRag.Application/Services/ChatService.cs`
- [x] 2.2 In `SearchSemanticAsync`, drop the `RewriteAsync` call and embed the model-provided `query` argument directly; set `_activeRewrittenQuery = query`

## 3. Remove test fakes and rewriter-specific tests

- [x] 3.1 Delete `tests/PrRag.Tests/FakeQueryRewriter.cs`
- [x] 3.2 Remove the `FakeQueryRewriter` instance and `services.AddSingleton<IQueryRewriter>(rewriter);` (plus the singleton and any now-unused usings) from `tests/PrRag.Tests/IntegrationServiceFactory.cs`
- [x] 3.3 Delete `Semantic_search_rewrites_query_with_full_conversation` and `Exact_match_tool_does_not_invoke_query_rewriter` from `tests/PrRag.Tests/AgenticRetrievalTests.cs` (including the now-unused `IQueryRewriter` using and `FakeQueryRewriter` references)
- [x] 3.4 Update `RagObservabilityReportTests.Report_written_with_question_parameters_and_answer` to assert `report.RewrittenQuery == "acme hydraulic pump"` (no `optimized:` prefix)

## 4. Verify no dangling references and run checks

- [x] 4.1 Confirm no references to `IQueryRewriter`, `SemanticQueryRewriter`, or `FakeQueryRewriter` remain in `src/` and `tests/` (grep the workspace)
- [x] 4.2 `dotnet build PrRag.sln` succeeds
- [x] 4.3 Run tests with a reachable Postgres (`TEST_CONNECTION_STRING="Host=localhost;Port=5432;Username=prrag;Password=prrag" dotnet test tests/PrRag.Tests`) and confirm the suite passes