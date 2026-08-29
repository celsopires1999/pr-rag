## Context

A greenfield educational reference project demonstrating how to build a RAG system on .NET 10. There is no existing code. The system answers natural-language questions over purchase requisitions (last 18 months) using PostgreSQL + pgvector vector search and OpenAI models.

Data arrives as a single regenerated JSON file containing structured, tabular records, each identified by `purchase_requisition` (10 chars) plus `supplier_code`, `supplier_name`, `item`, `item_name`, and `description` (500 chars). The reference must show both halves of RAG: an incremental ingestion pipeline and a chat/query pipeline, all running inside containers.

## Goals / Non-Goals

**Goals:**
- Demonstrate idiomatic .NET 10 RAG using `Microsoft.Extensions.AI` + OpenAI and EF Core + Npgsql + `Pgvector.EntityFrameworkCore`.
- Incremental ingestion from a bind-mounted JSON file (auto-watch + manual trigger) that upserts by natural key and selectively re-embeds changed rows.
- Chat API (`POST /api/chat`) that embeds the question, runs a thresholded vector search, and returns an LLM answer grounded in retrieved context.
- Configurable models, API key, and RAG defaults via `IConfiguration`/environment; key provided at runtime via `.env`, never committed.
- Credit-health of a demo: layered projects, integration test for the ingest diff, README with run steps and `curl` examples.
- All code and docs in English.

**Non-Goals:**
- No user-facing UI or CLI; API-only delivery. Non-streaming (full) responses; no SSE.
- No authentication/authorization, multi-tenancy, or production hardening (this is a demo reference).
- No historical versioning of rows; upsert overwrites all fields of the existing `purchase_requisition`.
- No support for multiple embedding strategies per row or vector versioning.

## Decisions

### 1. Layered solution structure
Two/three projects: `PrRag.Api` (host/HTTP), `PrRag.Application` (application/Core logic: services, models, interfaces), `PrRag.Infrastructure` (EF Core, Npgsql, pgvector, file watcher, OpenAI/embedding adapters). Rationale: separates HTTP from business logic and infrastructure, making the reference easier to read and the diff test focused on application logic without HTTP/database coupling.

### 2. `Microsoft.Extensions.AI` + OpenAI
Use `Microsoft.Extensions.AI` with `Microsoft.Extensions.AI.OpenAI` provider for both chat and embeddings. Rationale: this is the modern, provider-agnostic idiomatic .NET abstraction; keeps the reference aligned with current platform guidance. Alternatives considered: the raw `OpenAI` SDK (more direct but less portable) — rejected to showcase the recommended pattern.

### 3. EF Core + Npgsql + `Pgvector.EntityFrameworkCore`
EF Core for data access with `Npgsql.EntityFrameworkCore.PostgreSQL` and `Pgvector.EntityFrameworkCore` for the vector column/type and HNSW index. Rationale: full migrations-first workflow and idiomatic integration. Alternatives considered: Dapper with raw SQL (would expose `<=>` visibly but loses migrations/type mapping) — rejected in favor of the richer, production-style stack.

### 4. Single-table vector storage (Opção A)
Embedding column lives in the same `purchase_requisitions` table (`embedding vector(1536)`), with an HNSW index. Rationale: simplest and performant for the always-join-with-row query pattern. Alternatives considered: a separate `pr_embeddings` table — rejected (no per-row multi-vector/versioning need).

### 5. Incremental ingestion via diff + upsert + selective re-embed
On each import run (whether auto-triggered or manual):
1. Load JSON and normalize records.
2. Diff against current DB rows by `purchase_requisition` to detect new/changed rows (compare relevant fields).
3. Upsert all records; embed and store the vector only for new/changed rows.
4. Re-embedding uses the concatenated source `supplier_name | item_name | description`.

Rationale: minimizes embedding API calls (cost/latency) by only embedding what changed. Alternatives considered: blind upsert + re-embed all rows (simpler tracking, wasteful) — rejected for efficiency.

### 6. Híbrid trigger: `FileSystemWatcher` + manual endpoint
`FileSystemWatcher` on the bind-mounted data file auto-runs ingestion on change; `POST /api/ingest` forces a re-import. Rationale: automatic demo behavior plus explicit, observable re-run to demonstrate the diff selectively re-embedding.

### 7. Query pipeline with configurable `top_k` and `min_similarity`
`POST /api/chat { question, top_k = 5, min_similarity = 0.7 }`:
1. Embed question with the configured embedding model.
2. Vector search (`ORDER BY embedding <=> @q`), filter by HNSW index and threshold, take top-K.
3. Build prompt with retrieved context.
4. Call chat model (full response; no streaming).
5. If no context passes the threshold, reply with a "not enough information" message.

Rationale: exposes the two main RAG control knobs for a didactic demo and handles the empty-context case gracefully.

### 8. Configuration via `IConfiguration`/environment
Config keys: `OpenAI:ApiKey`, `OpenAI:EmbeddingModel` (default `text-embedding-3-small`), `OpenAI:ChatModel` (default `gpt-4o-mini`), `RAG:TopK` (5), `RAG:MinSimilarity` (0.7), plus connection string and data-file path. API key injected via `.env`/environment, never committed. Embedding model choice fixes the vector dimension used at migration time.

### 9. Synthetic data generator
A console/script project generates `purchase.json` (~3k rows over 18 months) with recurring suppliers and items so semantic queries (`"Acme widgets"`) are meaningful. The generator writes the file into the bind-mount path, exercising the true ingest path. Rationale: keeps the demo faithful to the real "file → ingest" narrative.

### 10. Observability endpoints
`/health` (DB reachability) and `/api/status` (row count, embedded count, last sync) to prove the pipeline. Rationale: makes the incremental behavior visible and self-explanatory in the demo.

## Risks / Trade-offs

- **Embedding dimension/model coupling** → Database migration defines `vector(1536)`; if the embedding model is changed to a different dimension (e.g., `text-embedding-3-large` = 3072), a migration/reindex is required. Mitigation: document this and keep the default fixed; expose dimension as a constant.
- **Cost of embedding thousands of rows** → ~3k rows is small, but repeated full imports could re-embed unnecessarily. Mitigation: the diff only re-embeds changed rows (Decision 5).
- **`FileSystemWatcher` in containers** → Depends on `inotify` events over the bind-mount; behavior can vary across host filesystems. Mitigation: the manual `POST /api/ingest` provides a reliable fallback, and the watcher is best-effort.
- **pgvector extension setup** → `postgres:18-alpine` lacks pgvector; using `pgvector/pgvector:pg18` addresses it, but the extension must be enabled (`CREATE EXTENSION vector`) on startup. Mitigation: run it in the DB init/migration path.
- **LLM non-determinism / grounding quality** → Answers depend on retrieved context quality. Mitigation: embed concatenated supplier|item|description for richer matching and expose `top_k`/`min_similarity` for tuning.

## Migration Plan

- Greenfield: `docker compose up` provisions the Postgres container; EF Core migrations create the `vector` extension, table, and HNSW index on startup (or via explicit `dotnet ef` command documented in README).
- Rollback: not applicable at this stage (no prior deployment); documented steps suffice for a fresh demo.

## Open Questions

- Whether to run EF Core migrations automatically on startup vs. an explicit documented `dotnet ef database update` step (default: automatic on startup for demo convenience, with README documenting both).
- Whether the watcher should debounce consecutive file writes (default: yes, short debounce + manual trigger as fallback).
