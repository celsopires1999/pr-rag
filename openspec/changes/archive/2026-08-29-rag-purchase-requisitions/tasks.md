## 1. Solution scaffolding and infrastructure

- [x] 1.1 Create the .NET 10 solution with layered projects: `PrRag.Api`, `PrRag.Application`, `PrRag.Infrastructure`
- [x] 1.2 Add NuGet packages: `Microsoft.Extensions.AI`, `Microsoft.Extensions.AI.OpenAI`, EF Core, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Pgvector.EntityFrameworkCore`
- [x] 1.3 Add `docker-compose.yml` with `pgvector/pgvector:pg18` database and API container, plus a bind-mount volume for the JSON data file
- [x] 1.4 Add infrastructure files (Dockerfiles, `.env.example`, `.gitignore` excluding secrets/`.env`)

## 2. Configuration and models

- [x] 2.1 Implement `IConfiguration`/environment settings: `OpenAI:ApiKey`, `OpenAI:EmbeddingModel` (default `text-embedding-3-small`), `OpenAI:ChatModel` (default `gpt-4o-mini`), `RAG:TopK` (5), `RAG:MinSimilarity` (0.7), connection string, and data-file path
- [x] 2.2 Define the domain model `PurchaseRequisition` with `purchase_requisition` (PK), `supplier_code`, `supplier_name`, `item`, `item_name`, `description`, and `embedding`

## 3. Data access (Infrastructure)

- [x] 3.1 Create EF Core `DbContext` with `Pgvector.EntityFrameworkCore` mapping for the `embedding vector(1536)` column
- [x] 3.2 Add EF Core migration creating the `vector` extension, `purchase_requisitions` table, and HNSW index on the embedding column
- [x] 3.3 Apply migrations on API startup (with README documenting explicit `dotnet ef` alternative)

## 4. Synthetic data generator

- [x] 4.1 Implement generator producing ~3k JSON records over the last 18 months with recurring suppliers/items
- [x] 4.2 Ensure generated records match the ingestion schema (`purchase_requisition`, `supplier_code`, `supplier_name`, `item`, `item_name`, `description`)
- [x] 4.3 Write the dataset to the bind-mounted data path

## 5. Ingestion pipeline (incremental)

- [x] 5.1 Implement JSON parsing/normalization of purchase requisitions
- [x] 5.2 Implement diff logic to detect new/changed rows by `purchase_requisition`
- [x] 5.3 Implement upsert (overwrite all fields) for existing rows and insert for new rows
- [x] 5.4 Implement selective re-embedding of only new/changed rows using `supplier_name | item_name | description` concatenation
- [x] 5.5 Add `FileSystemWatcher` on the data file for automatic ingestion with debounce
- [x] 5.6 Add `POST /api/ingest` manual trigger endpoint

## 6. Chat query pipeline

- [x] 6.1 Implement request/response DTOs for `/api/chat` with `question`, `top_k` (default 5), `min_similarity` (default 0.7)
- [x] 6.2 Implement question embedding via configured embedding model
- [x] 6.3 Implement thresholded vector search (top-K above `min_similarity`) using EF Core/pgvector
- [x] 6.4 Build prompt from retrieved context and call the configured chat model (full, non-streaming response)
- [x] 6.5 Handle empty-context case with a "not enough information" response

## 7. Observability endpoints

- [x] 7.1 Add `/health` endpoint checking database reachability
- [x] 7.2 Add `/api/status` endpoint reporting stored requisition count, embedded count, and last sync time

## 8. Tests

- [x] 8.1 Add integration test project covering the ingest diff: initial import embeds all rows
- [x] 8.2 Add integration test verifying re-import with no changes does not re-embed existing rows
- [x] 8.3 Add integration test verifying changed/new rows are upserted and re-embedded

## 9. Documentation

- [x] 9.1 Write comprehensive `README.md` with setup steps (`docker compose up`, generate data, run API), configuration, and example `curl` calls for `/api/chat`, `/api/ingest`, and `/api/status`
