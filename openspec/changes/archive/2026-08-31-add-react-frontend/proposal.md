## Why

Today the RAG application is API-only: it exposes `/api/chat`, `/api/ingest` and `/api/status`, but there is no user-facing interface. To demo and validate the RAG behavior, a human operator must hand-craft HTTP calls. Adding a lightweight React front-end makes the system usable end-to-end: ask a question, kick off an ingestion, and see system status in one place.

## What Changes

- Add a new `PrRag.Web` React (Vite + TypeScript) front-end project that talks to the existing API endpoints.
- Add `features/web` Chat UI: input a question (with optional RAG `top_k` / `min_similarity` overrides), POST to `/api/chat`, and render the answer.
- Add an ingestion trigger button that calls POST `/api/ingest` and shows the ingest result (inserted/updated/embedded counts).
- Add a system status panel that calls GET `/api/status` and shows requisition/embedded counts and last sync, auto-refreshing.
- Add CORS support to the API so the browser front-end can call it cross-origin.
- Add a `web` service to `docker-compose.yml` (behind the `demo` profile, matching the existing `api` service) and the Dockerfiles/build wiring.
- Serve static files from the API (optional build/run option) so a single process can host both API and UI.

### Not in scope

- Authentication / multi-user support.
- A production-graded front-end framework or SSR beyond Vite's dev server.
- Changing existing API endpoint contracts (they stay backward compatible).

## Capabilities

### New Capabilities

- `web-frontend`: The React user interface for chatting with the RAG system, triggering ingestion, and viewing system status, plus the client-server CORS wiring and container integration.

### Modified Capabilities

<!-- None: the three existing capabilities (chat-query, data-ingestion, rag-observability-report) keep their API contracts unchanged. -->

## Impact

- **New project**: `web/` (or `src/PrRag.Web`) with Vite, React, TypeScript; own `package.json`.
- **API host** (`src/PrRag.Api/Program.cs`): add CORS policy; optionally serve static front-end files.
- **Containerization**: new `Dockerfile.web` (or a multi-stage build) and a `web` service in `docker-compose.yml` under the `demo` profile; update `.env` for any `WEB_PORT`.
- **Docs**: `README` and `AGENTS.md` updates describing how to run the front-end.
- **No changes** to `PrRag.Application` or `PrRag.Infrastructure` domain logic; API responses (`ChatResponse`, `IngestResult`, `SystemStatus`) already contain all data the UI needs.
