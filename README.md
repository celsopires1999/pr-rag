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

```bash
docker compose up --build
```

This starts:
- **db** — `pgvector/pgvector:pg18` (creates schema via EF Core migrations on API startup)
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
