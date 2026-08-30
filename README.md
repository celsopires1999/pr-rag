# PrRag — RAG on .NET 10 with PostgreSQL + pgvector + OpenAI

A back-end reference/demo showing how to build a **Retrieval-Augmented Generation (RAG)** system in **.NET 10**. It answers natural-language questions over **purchase requisitions** (last 18 months) using vector search in PostgreSQL (`pgvector`) and OpenAI models.

Everything runs in Docker containers — no local .NET SDK required.

## Highlights

- **.NET 10** API (`Microsoft.Extensions.AI` + OpenAI), EF Core + Npgsql + `Pgvector.EntityFrameworkCore`
- **PostgreSQL 18** with `pgvector` (HNSW index) storing requisitions + their embeddings
- **Incremental ingestion**: automatic file watch + manual trigger; diffs and re-embeds only changed rows
- **Chat query** with configurable `top_k` and `min_similarity`
- **Synthetic data generator** for a realistic demo dataset (~3k rows over 18 months)
- Layered solution + integration tests for the ingest diff

## Architecture

```
┌──────────────┐   purchase.json (bind-mount)
│ Generator     │──────────▶ ┌────────────────────────────────────┐
│ (synthetic)   │            │  API (.NET 10)                     │
└──────────────┘            │                                    │
                            │  ┌────────────┐  ┌──────────────┐   │
 ┌──────────────────────┐   │  │ Ingestion  │  │   Chat       │   │
 │ PostgreSQL 18 (pgvec)│◀──┼──│  (diff /    │  │  (vector     │   │
 │ purchase_requisitions │   │  │  upsert /  │  │   search +   │   │
 │  + embedding + HNSW   │   │  │  re-embed) │  │   OpenAI)    │   │
 └──────────────────────┘   │  └────────────┘  └──────────────┘   │
                            └────────────────────────────────────┘
```

## Prerequisites

- Docker + Docker Compose (with Compose v2, the `docker compose` command)
- An OpenAI API key (`OPENAI_API_KEY`)

## Quick start

### 1. Configure environment

```bash
cp .env.example .env
# edit .env and set OPENAI_API_KEY
```

### 2. Start the stack

By default only the database is started as a long-running service:

```bash
docker compose up -d
```

This starts:
- **db** — `pgvector/pgvector:pg18` (creates schema via EF Core migrations on API startup)

The **api** (and its `OpenAI__ApiKey`) is guarded behind the `demo` profile, so the key is never resolved into `docker compose config` during everyday development. Start it explicitly only when you need a full runtime demo:

```bash
docker compose --profile demo up --build api
```

This additionally starts:
- **api** — the .NET 10 API on `http://localhost:8080`

### 3. Generate synthetic data

Generate the purchase requisitions JSON into the bind-mounted `./data` folder:

```bash
docker compose --profile tools run --rm datagen /data/purchase.json
```

> The generator is a small console project (`tools/PrRag.DataGenerator`). You can build/run it directly with the .NET 10 SDK if you have it:
> ```bash
> dotnet run --project tools/PrRag.DataGenerator -- data/purchase.json
> ```

The API watches `data/purchase.json` and ingests it automatically. You can also trigger ingestion manually (see below).

### 4. Ask a question

```bash
curl -X POST http://localhost:8080/api/chat \
  -H "Content-Type: application/json" \
  -d '{"question": "Which suppliers provide hydraulic pumps?"}'
```

## API

### `POST /api/chat`

Embeds the question, runs a thresholded vector search over requisitions, and returns an OpenAI answer grounded in the retrieved context.

Request:

```json
{
  "question": "Which suppliers provide hydraulic pumps?",
  "top_k": 5,           // optional (default: RAG:TopK = 5)
  "min_similarity": 0.7 // optional (default: RAG:MinSimilarity = 0.7)
}
```

Response:

```json
{
  "answer": "Acme Industrial Supply and Beta Components Ltd provide hydraulic pumps...",
  "retrievedCount": 3
}
```

If no requisitions meet the similarity threshold, the answer is:

```json
{ "answer": "I don't have enough information in the purchase requisitions to answer that.", "retrievedCount": 0 }
```

### `POST /api/ingest`

Manually re-imports the current JSON file (diff + upsert + selective re-embed):

```bash
curl -X POST http://localhost:8080/api/ingest
```

```json
{
  "totalRecords": 3000,
  "inserted": 3000,
  "updated": 0,
  "embedded": 3000
}
```

> On a second run with no changes, `embedded`/`updated` will be `0` — proof that only changed rows are re-embedded.

### `GET /api/status`

Reports ingestion state:

```bash
curl http://localhost:8080/api/status
```

```json
{
  "requisitionCount": 3000,
  "embeddedCount": 3000,
  "lastSync": "2026-08-29T18:00:00Z"
}
```

### `GET /health`

Database reachability health check:

```bash
curl http://localhost:8080/health
```

## Configuration

All settings come from environment variables / `IConfiguration` (see `.env.example`).

| Variable | Section key | Default |
|---|---|---|
| `OPENAI_API_KEY` | `OpenAI:ApiKey` | *(required)* |
| `OPENAI_EMBEDDING_MODEL` | `OpenAI:EmbeddingModel` | `text-embedding-3-small` |
| `OPENAI_CHAT_MODEL` | `OpenAI:ChatModel` | `gpt-4o-mini` |
| `RAG_TOP_K` | `RAG:TopK` | `5` |
| `RAG_MIN_SIMILARITY` | `RAG:MinSimilarity` | `0.7` |
| `POSTGRES_USER/PASSWORD/DB` | `ConnectionStrings:Default` | `prrag` |
| — | `Data:FilePath` | `/data/purchase.json` |

> **Note:** the embedding model determines the vector dimension (1536 for `text-embedding-3-small`). Changing to a model with different dimensions requires a new migration/reindex.

## How incremental ingestion works

1. The JSON file is loaded and normalized.
2. Existing rows are compared by `purchase_requisition` (primary key).
3. **New** and **changed** rows are the only ones embedded.
4. Embeddings are generated from the concatenated source `supplier_name | item_name | description`.
5. All records are upserted (overwrites all fields for existing rows); unchanged rows keep their current embedding.

This keeps embedding API calls minimal. The trigger is hybrid: a `FileSystemWatcher` auto-ingests on file change (with debounce) and `POST /api/ingest` forces a re-import on demand.

## Running the tests

The integration tests verify the ingest diff behavior against a real Postgres instance (started as part of the stack):

```bash
# with db running, via the tests container
docker compose -f docker-compose.yml -f docker-compose.test.yml --profile test up --build test
```

Or run directly with the .NET 10 SDK (requires a reachable Postgres):

```bash
TEST_CONNECTION_STRING="Host=localhost;Port=5432;Username=prrag;Password=prrag" \
  dotnet test tests/PrRag.Tests
```

Covered scenarios:
- Initial import embeds all rows.
- Re-import with no changes does **not** re-embed.
- Changed/new rows are upserted and re-embedded.

## Development environment (DevContainer)

For an out-of-the-box development experience, this repository ships a **DevContainer** (VS Code / GitHub Codespaces) that provides the .NET 10 SDK, recommended extensions, and ready-to-use debug/task configuration — **no `dotnet` install on your host is required**. Everything (build, run, test, debug) happens inside the container, reusing the `pgvector` database service from the compose stack.

### Prerequisites

- **Docker + Docker Compose v2** running on your host.
- **VS Code** with the **Dev Containers** extension (or GitHub **Codespaces**).
- An **OpenAI API key** in `.env` if you plan to run/debug the API (see [Quick start](#quick-start)).

### First-time Open

1. Install the **Dev Containers** extension for VS Code.
2. Open the repository folder.
3. Run **"Reopen in Container"** (Command Palette → `Dev Containers: Reopen in Container`).

On the first open VS Code pulls/builds the `.NET 10` SDK image — this can take a few minutes the first time. When ready, a terminal opens inside the container rooted at `/workspaces`.

Opening the container starts the `devcontainer` service (defined in `docker-compose.yml`), which also starts the `db` service (`pgvector`) automatically. The recommended extensions (C#, C# Dev Kit, Docker, PostgreSQL, EditorConfig) are installed automatically into the container.

> Requires an `OPENAI_API_KEY` set in `.env` to run/debug the API. The key is read via `envFile: .env` and is never committed.

### Setting up `.env`

The `.env` file (git-ignored) supplies the secrets/configuration used by the API and the dev container:

```bash
cp .env.example .env
# edit .env and set OPENAI_API_KEY
```

For the F5 debug launch to pick up the key, `.env` must also expose it under the .NET config key the app reads. The `.env` therefore contains **both** forms:

```ini
OPENAI_API_KEY=sk-...          # compose/runtime (OPENAI_ prefix)
OpenAI__ApiKey=sk-...          # .NET config key|debug reads via envFile
```

(`.env.example` documents these entries; keep the two values in sync.) The debug/task configuration loads `.env` via `envFile`, so no API key is injected into the `devcontainer` service environment or printed by `docker compose config`.

Changes to `.env` are picked up by relaunching the API from VS Code (the debug/task configuration loads it via `envFile`).

### Building, running and testing

Inside the container a normal `dotnet` workflow works:

```bash
dotnet restore PrRag.sln     # restore packages
dotnet build PrRag.sln       # build the solution
dotnet run --project src/PrRag.Api   # run the API
dotnet test tests/PrRag.Tests        # run the tests
```

Or use the pre-configured VS Code tasks (all run the in-container SDK):

- **Build** — `Ctrl/Cmd+Shift+B` (`dotnet build PrRag.sln`).
- **Watch** — Terminal → **Run Task…** → `build`/`watch` to rebuild on change and run `dotnet watch`.
- **Test** — Terminal → **Run Task…** → `test` to run the test suite.

### Debugging (F5)

Press **F5** — the `PreRag.Api (DevContainer)` launch configuration:

1. Runs the `build` task (so the API is up to date).
2. Starts `PrRag.Api` in **Debug** using the in-container SDK.
3. Connects to the `db` service (`ConnectionStrings__Default` from `.env`).
4. Serves on `http://0.0.0.0:8080` (`ASPNETCORE_URLS` is set) and the port is forwarded to your host, so you can hit `http://localhost:8080` from your browser.

Set breakpoints and step through the code as usual.

### Using the database from the container

The container includes the `postgresql-client`, so you can inspect the `db` service directly:

```bash
psql "postgresql://prrag:prrag@db:5432/prrag"
```

Useful queries:

```sql
SELECT count(*) FROM purchase_requisitions;          -- stored requisitions
SELECT count(*) FROM purchase_requisitions WHERE embedding IS NOT NULL; -- embedded
```

### Generating data

The generator and API keep using the bind-mounted `./data` folder. From the container you can generate the demo dataset with:

```bash
docker compose --profile tools run --rm datagen /data/purchase.json
```

The API auto-ingests the new file (file watcher) or you can trigger ingestion manually:

```bash
curl -X POST http://localhost:8080/api/ingest
```

### Running the integration tests

Tests run against the `db` service from the compose stack. From the container, either:

```bash
dotnet test tests/PrRag.Tests
```

or, to run them via the dedicated test container (no `dotnet` on the host), from the repository root on the host:

```bash
docker compose -f docker-compose.yml -f docker-compose.test.yml --profile test up --build test
```

> Note: the integration tests currently need the `vector` extension present in the target database of the ephemeral `prrag_test_*` databases (a known, tracked follow-up).

### Troubleshooting

- **Port 8080 already in use** — stop other containers or change the forwarded port in `.devcontainer/devcontainer.json` (`forwardPorts`).
- **Stale container** — reopen the container again (rebuild) or run `docker compose build devcontainer`.
- **API can't reach the DB** — confirm the `db` service is healthy with `docker compose ps` and that `.env` has the `ConnectionStrings__Default` pointing to `db`.
- **OpenAI errors** — verify `OPENAI_API_KEY` is set and valid in `.env`.

## Project structure

```
PrRag.sln
├── src/
│   ├── PrRag.Application/      # domain, DTOs, service interfaces + logic
│   ├── PrRag.Infrastructure/   # EF Core, pgvector, OpenAI client, file watcher
│   └── PrRag.Api/              # ASP.NET Web API host + endpoints
├── tests/PrRag.Tests/          # ingest diff integration tests
├── tools/PrRag.DataGenerator/  # synthetic data generator
├── data/                       # bind-mounted into the API (/data/purchase.json)
├── docker-compose.yml
├── docker-compose.test.yml
├── Dockerfile
└── .env.example
```

## EF Core migrations

Migrations are applied automatically on API startup. To manage them explicitly (if you have the .NET 10 SDK), from `src/PrRag.Api`:

```bash
dotnet ef migrations add <Name> --project ../PrRag.Infrastructure/PrRag.Infrastructure.csproj
dotnet ef database update --project ../PrRag.Infrastructure/PrRag.Infrastructure.csproj
```
