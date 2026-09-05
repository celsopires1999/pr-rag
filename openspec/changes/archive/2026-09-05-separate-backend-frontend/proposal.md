## Why

The repository root mixes the .NET back-end (`src/`, `tests/`, `tools/`, `PrRag.sln`), the web front-end (`web/`), and their Dockerfiles with no visible boundary between the two halves. New contributors and tooling have to infer the split from file contents, which is slower and more error-prone than an explicit layout.

## What Changes

- **BREAKING** Move the .NET solution and projects (`PrRag.sln`, `src/`, `tests/`, `tools/`) into a new top-level `backend/` directory. The solution relocates together with all projects, so its internal relative references stay valid.
- **BREAKING** Rename the front-end directory from `web/` to `frontend/` (Vite + React + TypeScript app, `nginx.conf`, README).
- Move the back-end Dockerfiles (`Dockerfile`, `Dockerfile.test`, `Dockerfile.datagen`) into `backend/` and the front-end `Dockerfile.web` into `frontend/`.
- Keep `docker-compose.yml` / `docker-compose.test.yml` at the repository root; update build contexts and per-service paths to the new directories.
- Update DevContainer config, VS Code tasks/launch, `AGENTS.md`, and `README.md` to reference the new paths.
- No runtime, API, or behavioral change — this is a pure repository reorganization.

## Capabilities

### New Capabilities
- `project-layout`: Contract for the top-level repository layout — the .NET back-end lives in `backend/`, the web front-end lives in `frontend/`, and shared infrastructure (compose, DevContainer, docs, shared `data/`/`reports/` volumes) stays at the root.

### Modified Capabilities
- `devcontainer-frontend-tooling`: scenarios referencing the front-end directory path change from `web/` to `frontend/`.

## Impact

- **Repo layout**: top-level restructuring of tracked paths (`src/`, `tests/`, `tools/`, `PrRag.sln`, `web/`, root Dockerfiles).
- **docker-compose.yml / docker-compose.test.yml**: build contexts switch to `./backend` / `./frontend`; volume and port mappings stay at root and remain unchanged.
- **Dockerfiles**: relocated; `COPY` paths resolve under the new build contexts, so their internal paths are preserved.
- **DevContainer**: `.devcontainer/devcontainer.json` compose reference and mounted tree are unaffected (repo root is still mounted at `/workspaces`), but VS Code `tasks.json` / `launch.json` paths must gain the `backend/` prefix.
- **Docs**: `AGENTS.md` and `README.md` verify commands and architecture notes need path updates.
- **Front-end**: `web/` → `frontend/`; `vite.config.ts` and `nginx.conf` have no cross-directory references, so only builds and container Dockerfile move.
- **Shared volumes stay at root**: `data/` (dataset bind-mount) and `reports/` (RAG observability) remain at the repo root, mounted by the container services.