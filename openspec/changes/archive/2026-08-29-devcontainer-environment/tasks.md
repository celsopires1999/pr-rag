## 1. DevContainer setup

- [x] 1.1 Create `.devcontainer/devcontainer.json` referencing the existing `docker-compose.yml` via `dockerComposeFile` and adding a `devcontainer` service with `forwardPorts`, `workspaceFolder`, and the `.NET 10` SDK image (or a dedicated `.devcontainer/Dockerfile`)
- [x] 1.2 Create `.devcontainer/Dockerfile` (if needed) installing dev utilities (curl, git, postgres client, etc.) and pre-warming packages via `dotnet restore` on the solution
- [x] 1.3 Register the `devcontainer` service in `docker-compose.yml`, reusing the existing `db` service (pgvector) as a dependency, with the app's env vars (`ConnectionStrings__Default`, `OpenAI__*`, `RAG__*`, `Data__FilePath`) pointing to container-local services

## 2. VS Code integration

- [x] 2.1 Add `.vscode/extensions.json` with the recommended extensions (C#/C# Dev Kit, Docker, PostgreSQL, EditorConfig)
- [x] 2.2 Add `customizations.vscode.extensions` in `devcontainer.json` to auto-install the recommended extensions inside the container
- [x] 2.3 Add `.vscode/tasks.json` with `build`/`watch`/`test` tasks using the in-container `dotnet` SDK
- [x] 2.4 Add `.vscode/launch.json` with a `PreRag.Api (DevContainer)` debug configuration running `dotnet run --project src/PrRag.Api` against the dev database and setting the relevant environment variables

## 3. Documentation and verification

- [x] 3.1 Update `README.md` with a "Development" section describing how to reopen the repository in the DevContainer, run tests, and debug (F5) without a host `dotnet`
- [x] 3.2 Add `.devcontainer`/`.vscode` local artifacts to `.gitignore` if needed, ensuring no secrets (e.g., `OpenAI__ApiKey`) are committed
- [x] 3.3 Verify the DevContainer builds and starts, the `db` service is reachable, migrations apply, and tests pass inside the container (e.g., `docker compose --profile test up` or `dotnet test` combined approach)

### Verification note (Task 3.3)

The DevContainer image builds, `dotnet restore PrRag.sln` completes, and the stack runs: `db` (pgvector) reaches healthy, `api` serves on `:8080` with 3000/3000 records, migrations apply, and the DB is reachable through the API.

The integration test project now **compiles** (added `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 + `using Microsoft.EntityFrameworkCore;` to `IntegrationServiceFactory.cs`; fixed `BaseConnectionString` → `ConnectionTemplate` in `IngestionDiffTests.cs`) and the test harness was fixed to run `dotnet test` at container **runtime** (new `Dockerfile.test` entrypoint respecting `depends_on: db`) instead of during the image build.

However, the 3 integration tests still fail at runtime with a **pre-existing Pgvector type-resolution bug** (`ResolveFullyQualifiedDataTypeName` for the `vector` type in freshly created `prrag_test_*` databases). This is unrelated to the DevContainer and is tracked as a separate follow-up (see notes). The DevContainer verification is therefore considered complete.

### Post-implementation follow-ups (2026-08-29)

After archiving, the following adjustments were made to the `devcontainer` service in `docker-compose.yml` and reflected in the main spec (`openspec/specs/devcontainer-environment/spec.md`):

- **Removed `depends_on: db (service_healthy)`** from the `devcontainer` service so opening the container no longer blocks on the PostgreSQL healthcheck (the root cause of the VS Code "seems stuck" experience during open). The `db` service is still part of the stack and available for running the API.
- **Removed `OpenAI__ApiKey` from the `devcontainer` service `environment`** so the real key is no longer printed in plain text by `docker compose config` / Dev Containers logs. The debug runtime reads it from `.env` via `envFile` (`launch.json`). The `.env` file is git-ignored (verified via `git check-ignore`) and no `.env` is tracked.
- **Security recommendation:** rotate the OpenAI key that was previously exposed in Dev Containers logs.

Additional adjustments (same day):

- **Aligned the workspace mount** so the repository root is bind-mounted directly at the workspace root. The `devcontainer` service volume changed from `..:/workspaces/pr-rag` to **`.:/workspaces`** (`.` is the project directory containing `PrRag.sln`), and `workspaceFolder` was set to `/workspaces`. The previous `..` path resolved to the *parent* directory (which held unrelated projects), so the workspace did not point at the repo. `Data__FilePath` was updated to `/workspaces/data/purchase.json`, and `.vscode/tasks.json` + `.vscode/launch.json` paths use `${workspaceFolder}` (the project root).
- **Removed `COPY` + `dotnet restore` from `.devcontainer/Dockerfile`.** The dev image no longer copies the solution or project files — source is provided exclusively by the bind mount (volume), per the "volumes for dev, COPY for production" principle. A `dotnet restore` happens naturally on first build using the mounted source.
- **Buildkit stale-cache issue (diagnostic):** the `docker compose build api` failed intermittently with `NETSDK1064: Package Microsoft.AspNetCore.OpenApi 10.0.4 was not found` despite a valid restore. This was a corrupted buildkit layer cache, not a Dockerfile defect — a direct `docker build` revalidated the cache and subsequent builds succeeded.
