## Context

The repo currently mixes both halves at the root: the .NET back-end (`PrRag.sln`, `src/`, `tests/`, `tools/`), the web front-end (`web/`), and per-half Dockerfiles (`Dockerfile`, `Dockerfile.test`, `Dockerfile.datagen`, `Dockerfile.web`) all sit beside shared infrastructure (`docker-compose.yml`, `.devcontainer/`, `.vscode/`, `AGENTS.md`, `README.md`, `data/`, `reports/`). Nothing in the layout states which files belong to which half.

This change is a pure reorganization. No runtime behavior, API contract, or data model changes.

## Goals / Non-Goals

**Goals:**
- Establish two top-level halves with self-evident ownership: `backend/` (.NET) and `frontend/` (Vite + React).
- Keep each half independently buildable/testable from within its directory.
- Keep shared orchestration (compose, DevContainer, docs) at the root so neither half owns the shared infrastructure.
- Preserve all build, test, run, and demo workflows after the move.

**Non-Goals:**
- No change to solution structure, project boundaries, namespaces, or dependency direction.
- No behavior change to the API, front-end UI, data pipeline, or Docker runtime images.
- No split of `docker-compose.yml` into per-half files (user decision: compose stays at root).
- No container-image rename or profile changes (the `demo` / `tools` / `test` profiles stay as-is).

## Decisions

### 1. Move the .NET solution and projects together into `backend/`
`PrRag.sln`, `src/`, `tests/`, and `tools/` move **as one unit** into `backend/`. The solution's internal relative project references (`src/...`, `tests/...`, `tools/...`) stay valid because the solution and all projects move together.
- Alternative considered: moving only `/src` and leaving the solution at root — rejected because it leaves back-end ownership split across the root and `backend/`.

### 2. Rename `web/` to `frontend/`
The directory already is the entire front-end; `frontend/` states ownership explicitly and parallels `backend/`.
- Alternative considered: keeping `web/` — rejected per user decision (`backend/ + frontend/`).

### 3. Per-half Dockerfiles move into their directories; compose stays at the root
- `Dockerfile` (API), `Dockerfile.test`, `Dockerfile.datagen` → `backend/`, with build context `./backend`. Their `COPY` paths (`PrRag.sln`, `src/`, `tests/`, `tools/`) resolve unchanged under the new context. The test runner's entrypoint path (`tests/PrRag.Tests/...`) also resolves unchanged.
- `Dockerfile.web` → `frontend/Dockerfile` (default name, no suffix needed now that it lives in its own directory), with build context `./frontend`. Its internal paths change: `COPY web/package.json web/package-lock.json ./` becomes `COPY package.json package-lock.json ./`, `COPY web/ ./` becomes `COPY . ./`, and `COPY web/nginx.conf ...` becomes `COPY nginx.conf ...`.
- `docker-compose.yml` and `docker-compose.test.yml` stay at the root; only `build.context` / `dockerfile` values change. Host-volume mounts (`./data`, `./reports`) are unaffected because compose still resolves them from the root.

### 4. DevContainer and VS Code config keep the root mount, with `backend/` path prefixes
`.devcontainer/devcontainer.json` still references `../docker-compose.yml` and mounts the repo root at `/workspaces`, so nothing changes there. VS Code `tasks.json` (`build`, `watch`, `test`) and `launch.json` (`program`, `cwd`, `envFile`) gain the `backend/` prefix on `${workspaceFolder}` paths.

### 5. Docs and shared data stay at the root
`AGENTS.md`, `README.md`, `data/`, and `reports/` remain at the root; only path references inside the docs update. `vite.config.ts` and `nginx.conf` have no cross-directory references, so they move verbatim.

## Risks / Trade-offs

- **Path references missed somewhere** → grep the whole tree for `web/`, `PrRag.sln`, `src/`, `Dockerfile` (docs, compose, devcontainer, VS Code, `.github` if any) and update before committing; verify with builds and `docker compose config`.
- **Git history/blame noise from large moves** → use `git mv` so history is preserved; land as a single focused commit.
- **DevContainer cached state at old paths** → recreate the container (standard Dev Containers "Rebuild") after the move so `/workspaces` picks up the new layout.
- **Containerized test path assumptions** → `Dockerfile.test` entrypoint is workspace-relative and unchanged under `./backend` context; validate by running the compose `test` profile.
- **Nothing in `backend/` may reference the root** → keep all compose-level secrets/mounts at the root and only pass them in via environment; this is already the pattern.

## Migration Plan

1. Move back-end sources and Dockerfiles into `backend/` with `git mv` (still one commit).
2. Rename `web/` → `frontend/` and move `Dockerfile.web` → `frontend/Dockerfile` with `git mv`; adjust its internal `COPY` paths.
3. Update `docker-compose.yml` / `docker-compose.test.yml` build contexts and dockerfile names.
4. Update VS Code `tasks.json` / `launch.json` paths; update `AGENTS.md` / `README.md`.
5. Verify: `dotnet build` from `backend/`, `npm ci && npm run build` from `frontend/`, `docker compose config` parses, and the `demo` / `test` profiles build.
6. Rollback: revert the single commit; the old paths are restored exactly.

## Open Questions

- None — naming (`backend/` + `frontend/`) and compose placement were confirmed by the user.