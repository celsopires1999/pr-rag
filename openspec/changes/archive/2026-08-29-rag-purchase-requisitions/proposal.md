## Why

We need an educational back-end reference demonstrating how to build a Retrieval-Augmented Generation (RAG) system on .NET 10. By answering natural-language questions over purchase requisition data (last 18 months) using PostgreSQL + pgvector and OpenAI models, the project shows the modern, idiomatic .NET approach to RAG end to end.

## What Changes

- Introduce a .NET 10 API that exposes a non-streaming `POST /api/chat` endpoint, which embeds the user question, performs a vector search over purchase requisitions, and returns an LLM-generated answer grounded in the retrieved context.
- Set up a PostgreSQL database (containerized, `pgvector/pgvector:pg18`) storing purchase requisitions relationally along with their embeddings (`vector(1536)`) and an HNSW index.
- Add an incremental ingestion pipeline driven by a bind-mounted JSON file: automatic `FileSystemWatcher` plus a manual `POST /api/ingest` endpoint. It diffs against the current state, upserts by `purchase_requisition`, and re-embeds only changed rows.
- Add a synthetic data generator producing a realistic `purchase.json` (~3k lines over 18 months) compatible with the ingestion schema.
- Provide scraping pipelines for querying, health/status, and ingest observability endpoints.
- Structure the codebase in layers and include an integration test covering the ingest diff behavior.
- Deliver a comprehensive README with step-by-step run instructions and example `curl` calls.
- All code and documentation in English.

## Capabilities

### New Capabilities

- `chat-query`: Natural-language question answering over ingested purchase requisitions via embeddings, vector search, and an OpenAI chat model. Exposes `top_k` and `min_similarity` controls and handles the empty-context case gracefully.
- `data-ingestion`: Incremental ingestion of purchase requisitions from a bind-mounted JSON file, including file watching, manual trigger, diff detection, upsert by primary key, and selective re-embedding of changed rows.
- `synthetic-data`: Generator producing realistic purchase-requisition JSON (~3k rows, 18 months, recurring suppliers/items) for seeding and demonstrating the pipeline.

### Modified Capabilities

<!-- None: all capabilities are new in this initial change. -->

## Impact

- **New solution**: .NET 10 solution with layered projects (Api, Application/Core, Infrastructure) using `Microsoft.Extensions.AI` + OpenAI, EF Core + Npgsql + `Pgvector.EntityFrameworkCore`.
- **Infrastructure**: `docker-compose` defining the database container (`pgvector/pgvector:pg18`) and the API container, plus a bind-mount volume for the JSON data file.
- **Configuration**: OpenAI model selection, API key, and RAG defaults via `IConfiguration`/environment (API key from `.env`, never committed).
- **Not affected**: no existing code is changed; this is a greenfield reference project.
