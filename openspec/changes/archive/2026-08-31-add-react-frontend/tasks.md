## 1. Scaffold & baseline

- [x] 1.1 Create `web/` Vite + React + TypeScript project (`npm create vite@latest web -- --template react-ts` or equivalent) with a minimal package manifest
- [x] 1.2 Verify `web/` builds (`npm install && npm run build`) and the dev server starts (`npm run dev`)

## 2. Client-side API layer

- [x] 2.1 Add `web/src/types.ts` with `ChatRequest`, `ChatResponse`, `IngestResult`, `SystemStatus` interfaces mirroring the .NET DTOs
- [x] 2.2 Add `web/src/api.ts` `fetch` wrapper for `chat`, `ingest`, and `status` including error handling and a configurable base URL

## 3. UI components

- [x] 3.1 Build the Chat panel: question input, optional `top_k`/`min_similarity` inputs, submit button, answer + retrieved-count display, and empty-question validation
- [x] 3.2 Build the Ingestion panel: ingest button, loading state, and display of inserted/updated/embedded counts from `IngestResult`
- [x] 3.3 Build the Status panel: render `RequisitionCount`, `EmbeddedCount`, and `LastSync`, with an automatic refresh interval (e.g. `setInterval`)
- [x] 3.4 Wire the three panels into the root component with shared state; render empty/loading/error states

## 4. API CORS support

- [x] 4.1 Add a config-driven CORS policy in `src/PrRag.Api/Program.cs` allowing `Cors__AllowedOrigins` (default `http://localhost:5173`)
- [x] 4.2 Verify CORS headers are emitted for an allowed origin (manual curl / browser fetch)

## 5. Containerization & orchestration

- [x] 5.1 Add `Dockerfile.web` (multi-stage: node build → static server/vite preview) for `web/`
- [x] 5.2 Add a `web` service to `docker-compose.yml` under the `demo` profile with `depends_on: api` and an origin matching its container URL
- [x] 5.3 Update `.env.example`/`.env` with `WEB_PORT` and `CORS__AllowedOrigins`
- [x] 5.4 Verify end-to-end with `docker compose --profile demo up --build web api` and confirm chat, ingest, and status work from the UI

## 6. Documentation & verification

- [x] 6.1 Update `README.md` and `AGENTS.md` with how to run the front-end in dev and containerized modes, and how to override the allowed origin
- [x] 6.2 Confirm `dotnet build PrRag.sln` and `dotnet test tests/PrRag.Tests` still pass (no .NET regressions)
