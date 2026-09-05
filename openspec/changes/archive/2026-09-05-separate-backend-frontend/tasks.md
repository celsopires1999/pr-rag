## 1. Back-end reorganization

- [x] 1.1 Use `git mv` to move `PrRag.sln`, `src/`, `tests/`, and `tools/` into `backend/` (keeping project-relative references intact)
- [x] 1.2 Use `git mv` to move `Dockerfile`, `Dockerfile.test`, and `Dockerfile.datagen` into `backend/`
- [x] 1.3 Confirm the back-end solution still builds: `dotnet build PrRag.sln` from `backend/`
- [x] 1.4 Confirm back-end tests resolve: `dotnet test tests/PrRag.Tests` from `backend/` (with a reachable database)

## 2. Front-end reorganization

- [x] 2.1 Use `git mv` to rename `web/` to `frontend/`
- [x] 2.2 Use `git mv` to move `frontend/Dockerfile.web` to `frontend/Dockerfile` (source was the root `Dockerfile.web`, moved to `frontend/Dockerfile`)
- [x] 2.3 Update the moved front-end `Dockerfile`: drop the `web/` prefix from `COPY` lines (`package.json`/`package-lock.json`, `.` source, and `nginx.conf`)
- [x] 2.4 Remove now-stale top-level `Dockerfile.web` reference from the repository root (file moved in 2.2)
- [x] 2.5 Confirm the front-end still builds: `cd frontend && npm install && npm run build`

## 3. Compose and container config

- [x] 3.1 In `docker-compose.yml`, change the `api` service build to `./backend` with dockerfile `Dockerfile`
- [x] 3.2 In `docker-compose.yml`, change the `datagen` service build to `./backend` with dockerfile `Dockerfile.datagen`
- [x] 3.3 In `docker-compose.yml`, change the `web` service build to `./frontend` with dockerfile `Dockerfile`
- [x] 3.4 In `docker-compose.test.yml`, change the `test` service build to `./backend` with dockerfile `Dockerfile.test`
- [x] 3.5 Update `.env.example` / `.env` comments if they reference old paths
- [x] 3.6 Confirm `docker compose config` parses and resolves all build contexts

## 4. DevContainer and VS Code

- [x] 4.1 Update `.vscode/tasks.json` `build`, `watch`, and `test` commands to prefix `${workspaceFolder}` paths with `backend/`
- [x] 4.2 Update `.vscode/launch.json` `program`, `cwd`, and task references to prefix paths with `backend/`
- [x] 4.3 Confirm `.devcontainer/devcontainer.json` still references the root compose/workspace and requires no change
- [x] 4.4 Rebuild the DevContainer so `/workspaces` reflects the new layout (bind mount verified; stale `web/.vite` cache removed)

## 5. Documentation updates

- [x] 5.1 Update `AGENTS.md` verify commands and architecture paths (`backend/PrRag.sln`, `backend/tests/PrRag.Tests`, `frontend/`)
- [x] 5.2 Update `README.md` structure, quick-start, and Docker commands for the new paths
- [x] 5.3 Update the `web/` references to `frontend/` everywhere in repo docs (incl. delta spec for `devcontainer-frontend-tooling` scenarios in the change)

## 6. Verification

- [x] 6.1 Full back-end build passes: `dotnet build PrRag.sln` from `backend/` (verified via devcontainer, same sources as 1.3)
- [x] 6.2 Front-end production build passes: `npm run build` in `frontend/` (verified via devcontainer, same sources as 2.5)
- [x] 6.3 Integration tests pass via compose: `docker compose -f docker-compose.yml -f docker-compose.test.yml --profile test up --build test`
- [x] 6.4 API and web containers build under the `demo` profile: `docker compose --profile demo build api web`
- [x] 6.5 Run `git status` to confirm no intended files remain outside `backend/` / `frontend/` and nothing is left behind erroneously