## 1. Report DTO

- [x] 1.1 Add `RagToolCall` DTO (`Name: string`, `Arguments: IReadOnlyDictionary<string, object?>`) in `backend/src/PrRag.Application/DTOs/RagReportDtos.cs`
- [x] 1.2 Add `List<RagToolCall> ToolCalls` property to `RagQueryReport` in the same file

## 2. Per-turn tracking

- [x] 2.1 Add `List<RagToolCall> ToolCalls { get; }` to `AgentTurnContext` in `backend/src/PrRag.Application/Services/Agents/AgentTurnContext.cs`
- [x] 2.2 Clear `ToolCalls` in `AgentTurnContext.Begin(...)`

## 3. Tool handler recording

- [x] 3.1 Add a `RecordToolCall(string name, IDictionary<string, object?> arguments)` helper to `PurchaseRequisitionTools` that appends a `RagToolCall` to `_turnContext.ToolCalls`
- [x] 3.2 Record invocation at the start of `SearchByCodesAsync` (arguments: `items`, `suppliers`)
- [x] 3.3 Record invocation at the start of `SearchSemanticAsync` (arguments: `query`)
- [x] 3.4 Record invocation at the start of `ActivateSkillAsync` (arguments: `name`)
- [x] 3.5 Record invocation at the start of `CreateRequisitionAsync` (arguments: `supplierCode`, `item`, `description`, `quantity`, `date`, `requester`)

## 4. Report wiring

- [x] 4.1 In `ChatService.WriteReportAsync` (`backend/src/PrRag.Application/Services/ChatService.cs`), assign `_turnContext.ToolCalls` to `report.ToolCalls`

## 5. Tests

- [x] 5.1 Update `RagObservabilityReportTests.Report_written_with_question_parameters_and_answer` to assert `report.ToolCalls` contains one record with `Name == "search_semantic"` and the `query` argument
- [x] 5.2 Update `RagObservabilityReportTests.Report_written_for_no_context_fallback` to assert `report.ToolCalls` is empty
- [x] 5.3 Add a test scripted with `search_by_codes` (and/or multiple scripted tool calls) asserting each invocation appears in order with its arguments

## 6. Verify

- [x] 6.1 `dotnet build backend/PrRag.sln`
- [x] 6.2 `TEST_CONNECTION_STRING="Host=localhost;Port=5432;Username=prrag;Password=prrag" dotnet test backend/tests/PrRag.Tests`