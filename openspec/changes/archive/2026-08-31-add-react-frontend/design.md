## Context

The repository is a layered .NET 10 solution (`PrRag.sln`) exposing three endpoints from `PrRag.Api` (`Program.cs`):

- `POST /api/chat` → `ChatResponse { Answer, RetrievedCount }` (request: `{ Question, top_k, min_similarity }`)
- `POST /api/ingest` → `IngestResult { TotalRecords, Inserted, Updated, Embedded }`
- `GET /api/status` → `SystemStatus { RequisitionCount, EmbeddedCount, LastSync }`

All response DTOs already carry the data a UI needs; no Application/Infrastructure changes are required. The API is containerized behind the `demo` profile in `docker-compose.yml`, and migrations auto-apply on startup. Today there is no web UI.

## Goals / Non-Goals

**Goals:**
- Ship a lightweight, developer-friendly React front-end to chat, trigger ingestion, and view status.
- Keep it aligned with the existing container model (Docker, `demo` profile).
- Minimal disruption to the existing API contracts and .NET code.

**Non-Goals:**
- Authentication / multi-user.
- Production CSS framework / design system.
- Changing the existing API request/response shapes.
- Server-side rendering or SSR frameworks.

## Decisions

### D1: Stack — Vite + React + TypeScript
Use Vite with the React + TypeScript template. It gives fast HMR for local dev, a simple static build, and a tiny footprint for containerization.

- **Alternative considered:** Next.js (heavier, SSR not needed here); plain HTML/JS (no component reuse, awkward to maintain). Chosen: Vite+React+TS, matching a generic modern SPA with no SSR requirement.

### D2: Project location and naming — `web/` at repo root
Create a self-contained `web/` directory with its own `package.json`, independent from the .NET solution. Rationale: avoids coupling the .NET solution file to a JS toolchain and keeps `dotnet build PrRag.sln` unaffected.

- **Alternative considered:** `src/PrRag.Web` inside the .NET solution — rejected to avoid confusion with the `PrRag.Web`/ASP.NET naming and to keep the .NET sln clean.

### D3: API consumption — plain `fetch` wrapper, no HTTP client library
One small `api.ts` module wrapping `fetch` for the three endpoints. Keeps dependencies minimal (the repo has no existing npm packages) and the code transparent.

### D4: Cross-origin access — CORS policy in the API
The API adds a CORS policy. Origin list comes from configuration (`Cors__AllowedOrigins`, defaults to `http://localhost:5173` for Vite dev). For the containerized demo, the allowed origin is the web service's origin. This keeps the API independently usable while allowing the browser UI to call it.

- **Alternative considered:** A Vite dev proxy only (works in dev, but the containerized UI would still be cross-origin). Using a real CORS policy covers both dev and container cases, so it is chosen.

### D5: Serving the built UI
Two supported modes:
1. **Dev**: Vite dev server + React HMR, API running separately; CORS bridges the two.
2. **Container (`demo` profile)**: Vite builds static assets; a lightweight static server (the Vite preview server via `vite preview`, or a tiny Node static server) serves `web/dist` in a `web` container. The UI points at the `api` service.

Static-file serving directly from the ASP.NET API was considered but deferred (avoids coupling the .NET publish to a JS build and keeps the two builds independent) — the container demo keeps them as two services.

### D6: UI structure — three logical panels, one page
A single-page layout with: a Chat panel (question + optional `top_k`/`min_similarity` inputs, submit, answer display), an Ingestion panel (ingest button + result counts), and a Status panel (auto-refreshing counts + last sync). Minimal state held in the root component (or a tiny `useState`-based store); no Redux.

### D7: Typed API models in TypeScript
Define `ChatRequest`, `ChatResponse`, `IngestResult`, `SystemStatus` interfaces mirroring the .NET DTOs so payloads are type-checked.

## Risks / Trade-offs

- **Dev/container origin mismatch** → Default allowed origin to `http://localhost:5173` for dev and the web service origin for the container; document the `Cors__AllowedOrigins` setting so operators can override.
- **Duplicate build tooling (npm + dotnet)** → Kept isolated in `web/`; `dotnet build PrRag.sln` is unaffected; CI/README documents the npm steps explicitly.
- **API stays unauthenticated** → Acceptable for a local/demo RAG tool; noted as a non-goal.
- **Serving via two containers adds moving parts** → Mitigated by documenting the exact Docker profile command and wiring the web container to `depends_on: api`.
- **CORS misconfiguration could silently block calls** → Clear error surface in the UI (show network failure) plus a documented env var.

## Migration Plan

1. Add `web/` scaffold and implement the UI (dev mode against the running API).
2. Add CORS policy to `Program.cs` (config-driven).
3. Add `Dockerfile.web` and `web` service to `docker-compose.yml` (under `demo` profile).
4. Update `.env` (e.g., `WEB_PORT`, `CORS__AllowedOrigins`) and README/AGENTS docs.
5. Verify: `docker compose --profile demo up --build web api`.

Rollback: the CORS change is additive and config-gated; disabling/reverting it does not break the existing API-only usage. The `web/` directory and `web` service can be removed without affecting the .NET app. No database migration is involved.

## Open Questions

- Should the API also serve the static UI in a future iteration (single process)? Deferred to D5/D6 — can be added later without breaking this design.
- Exact default `WEB_PORT` / `CORS__AllowedOrigins` values — to confirm with the operator's port plan (defaults proposed: `5173` dev, container service name/web origin in compose).
