## Why

The `OpenAiQueryRewriter` implementation currently lives in the Infrastructure layer even though it has zero Infrastructure-specific dependencies — it only depends on the provider-agnostic `IChatClient` abstraction (from `Microsoft.Extensions.AI.Abstractions`, already referenced by Application) and the `IQueryRewriter` interface (already in Application). This placement is inconsistent with the rest of the layer conventions and the codebase's stated goal that Application holds domain/business logic. Moving it to Application keeps pure LLM-backed business logic in the layer that owns it, and removes a misleading `OpenAi*` name from a class that is provider-agnostic.

## What Changes

- Move the `OpenAiQueryRewriter` implementation out of `PrRag.Infrastructure` into `PrRag.Application`.
- Rename the class from `OpenAiQueryRewriter` to `SemanticQueryRewriter` to reflect that it is provider-agnostic (works with any `IChatClient`), not OpenAI-specific.
- Move its DI registration from `AddInfrastructure()` to `AddApplication()`.
- Delete the old Infrastructure file.
- No package references change: Application already references `Microsoft.Extensions.AI.Abstractions` (needed for `IChatClient`/`ChatMessage`/`ChatRole`).
- No behavioral changes to the rewriter itself; the `IQueryRewriter` interface and all tests are unaffected.

## Capabilities

### New Capabilities
<!-- None. This is a pure architectural relocation with no new behavioral capability. -->

### Modified Capabilities
<!-- None. No spec-level requirement changes — the rewriter behaves identically after relocation. -->

## Impact

- **Code**: `src/PrRag.Infrastructure/Services/OpenAiQueryRewriter.cs` removed; new `src/PrRag.Application/Services/SemanticQueryRewriter.cs`; DI in `src/PrRag.Infrastructure/DependencyInjection.cs` and `src/PrRag.Application/DependencyInjection.cs`.
- **APIs**: Public `IQueryRewriter` interface unchanged. The concrete class is internal implementation detail.
- **Dependencies**: None added to Application (`Microsoft.Extensions.AI.Abstractions` already present).
- **Tests**: `FakeQueryRewriter` and `AgenticRetrievalTests` target `IQueryRewriter`, so no test changes.
