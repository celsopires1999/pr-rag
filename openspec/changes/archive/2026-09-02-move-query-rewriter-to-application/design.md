## Context

The rewriter is split across layers: the `IQueryRewriter` interface lives in `PrRag.Application/Abstractions/`, while the concrete implementation (`OpenAiQueryRewriter`) lives in `PrRag.Infrastructure/Services/`. An audit of `OpenAiQueryRewriter` shows it has **zero** Infrastructure-specific dependencies: it only uses `System.Text` (BCL), `Microsoft.Extensions.AI` (for `IChatClient`, `ChatMessage`, `ChatRole`), and `PrRag.Application.Abstractions` (for `IQueryRewriter`). All of the `Microsoft.Extensions.AI.Abstractions` types it needs are already referenced and used by the Application project (`PrRag.Application.csproj`).

Because the concrete implementation is pure LLM-backed business logic with no infrastructure coupling, its placement in Infrastructure is inconsistent with the codebase's layering conventions — Application should hold this logic.

## Goals / Non-Goals

**Goals:**
- Relocate the concrete query rewriter implementation from Infrastructure to Application.
- Rename it to a provider-agnostic name (`SemanticQueryRewriter`).
- Keep the rewriter's external contract (`IQueryRewriter`) and behavior identical.
- Ensure no new package dependency is added to Application.

**Non-Goals:**
- Changing the rewriter's prompt, rewrite logic, or behavior.
- Changing `IQueryRewriter`'s signature or the `query-rewriter` spec requirements.
- Touching OpenAI/`IChatClient` registration (stays in Infrastructure, which owns provider wiring).
- Modifying tests (they target `IQueryRewriter`).

## Decisions

**D1. Move the implementation to Application.** The class depends only on `IChatClient` (a cross-cutting `Microsoft.Extensions.AI.Abstractions` abstraction) plus the interface it implements. Application already references that package, so the move introduces no cycle and no new dependency.
- *Alternative considered:* Keep it in Infrastructure (status quo) — rejected because it misplaces pure business logic and retains a misleading name.
- *Alternative considered:* Move it to a new package — overkill for a single class; unnecessary churn.

**D2. Rename `OpenAiQueryRewriter` → `SemanticQueryRewriter`.** The implementation is provider-agnostic (it uses `IChatClient`, not the OpenAI SDK directly); the `OpenAi*` prefix is misleading. `SemanticQueryRewriter` reflects that it produces semantic-search queries.
- *Alternative considered:* Keep the `OpenAi*` name — rejected per the stated intent to remove the misleading name.

**D3. Move the DI registration to `AddApplication()`.** `AddScoped<IQueryRewriter, SemanticQueryRewriter>()` moves from `PrRag.Infrastructure/DependencyInjection.cs` to `PrRag.Application/DependencyInjection.cs`. Dependency resolution happens at runtime, when `IChatClient` (registered by `AddInfrastructure()`) is already available, so registration order (`AddApplication()` before `AddInfrastructure()` in `Program.cs`) is not a problem.

**D4. Use namespace `PrRag.Application.Services`.** Matches existing Application services layout and the new file location `src/PrRag.Application/Services/SemanticQueryRewriter.cs`.

## Risks / Trade-offs

- [Application layer now contains a class named after a behavior (`Semantic...`) — acceptable, it is an Application concern.]
- [Provider coupling risk: while `SemanticQueryRewriter` only uses `IChatClient`, the `SystemPrompt` assumes OpenAI-flavored chat behavior. This pre-exists and is out of scope.] → No action; noted as a non-goal.
- [Moved DI registration could be mistaken as requiring `IChatClient` at Application registration time.] → DI resolves lazily; covered by existing integration tests.
- [File move could orphan references.] → `dotnet build` inside the devcontainer verifies no dangling `OpenAiQueryRewriter` references remain.
