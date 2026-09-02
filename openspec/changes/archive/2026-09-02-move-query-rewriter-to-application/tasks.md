# Tasks

> **Command execution convention**: All `dotnet build` / `dotnet test` / `dotnet ef` commands
> run **inside the `devcontainer` container**, as the `vscode` user, from the workspace mounted at
> `/workspaces`. Do NOT run these on the host. Use the form:
>
> ```bash
> docker compose exec -u vscode -w /workspaces devcontainer dotnet build /workspaces/PrRag.sln
> ```
>
> The Postgres-backed integration tests additionally need the `db` container reachable (set
> `TEST_CONNECTION_STRING` or rely on the compose `test` profile). See `AGENTS.md`.

## 1. Create the new Application implementation

- [x] 1.1 Create `src/PrRag.Application/Services/SemanticQueryRewriter.cs` by copying the current `OpenAiQueryRewriter` implementation from `src/PrRag.Infrastructure/Services/OpenAiQueryRewriter.cs`.
- [x] 1.2 Rename the class `OpenAiQueryRewriter` → `SemanticQueryRewriter` and set the namespace to `PrRag.Application.Services`.
- [x] 1.3 Keep the implementation identical otherwise: same `SystemPrompt`, `IChatClient` field, and `RewriteAsync` body. Update `using` statements (`System.Text`, `Microsoft.Extensions.AI`, `PrRag.Application.Abstractions`) as needed.
- [x] 1.4 Delete the old file `src/PrRag.Infrastructure/Services/OpenAiQueryRewriter.cs`.

## 2. Move DI registration

- [x] 2.1 Remove `services.AddScoped<IQueryRewriter, OpenAiQueryRewriter>();` from `src/PrRag.Infrastructure/DependencyInjection.cs`.
- [x] 2.2 Add `services.AddScoped<IQueryRewriter, SemanticQueryRewriter>();` to `src/PrRag.Application/DependencyInjection.cs` (requires a `using PrRag.Application.Abstractions;` for `IQueryRewriter`).

## 3. Verify

- [x] 3.1 Build the solution inside the devcontainer:
      `docker compose exec -u vscode -w /workspaces devcontainer dotnet build /workspaces/PrRag.sln -c Debug`
- [x] 3.2 Confirm no lingering references to `OpenAiQueryRewriter` remain in `src/` (grep the workspace).
- [x] 3.3 Run the integration tests inside the devcontainer with a reachable Postgres,
      e.g. `docker compose exec -u vscode -w /workspaces devcontainer env TEST_CONNECTION_STRING="Host=db;Port=5432;Username=prrag;Password=prrag" dotnet test tests/PrRag.Tests` (or the `docker compose --profile test up --build test` flow).
- [x] 3.4 Confirm `FakeQueryRewriter` and `AgenticRetrievalTests` still pass without modification.

> **Note on 3.4**: Because the `IQueryRewriter → SemanticQueryRewriter` registration now lives in
> `AddApplication()` (which the integration test factory calls), the fake registration in
> `tests/PrRag.Tests/IntegrationServiceFactory.cs` must now appear **after** `AddApplication()` so it
> wins (Microsoft DI: last registration wins). The fake registrations were moved below
> `services.AddApplication();` accordingly. All 11 tests pass.
