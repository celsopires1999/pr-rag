## ADDED Requirements

### Requirement: Top-level back-end / front-end separation
The repository SHALL organize its source into two top-level directories: `.NET` back-end code under `backend/` and the web front-end under `frontend/`. Everything specific to a single half (projects, solution file, tests, tools, per-half Dockerfiles, front-end app) SHALL live inside its own directory.

#### Scenario: Back-end source under backend/
- **WHEN** a contributor browses the repository root
- **THEN** the entire .NET solution (`PrRag.sln`, `src/`, `tests/`, `tools/` and the back-end `Dockerfile`s) is nested under the `backend/` directory

#### Scenario: Front-end source under frontend/
- **WHEN** a contributor browses the repository root
- **THEN** the Vite + React + TypeScript app and its container files (Dockerfile, nginx configuration) live under the `frontend/` directory

### Requirement: Back-end solution remains self-contained
The .NET solution inside `backend/` SHALL build, restore, and test without referencing files outside `backend/`, so the directory can be treated as an independent back-end unit.

#### Scenario: Build from the back-end root
- **WHEN** a contributor runs `dotnet build PrRag.sln` inside `backend/`
- **THEN** the solution restores and builds successfully on its own

#### Scenario: Tests run from the back-end root
- **WHEN** a contributor runs `dotnet test tests/PrRag.Tests` inside `backend/` with a reachable database
- **THEN** the integration tests run to completion

### Requirement: Shared orchestration stays at the repository root
Compose files, the DevContainer configuration, top-level documentation (`README.md`, `AGENTS.md`), and shared runtime volumes (`data/`, `reports/`) SHALL remain at the repository root, driving both halves without being owned by either.

#### Scenario: Compose builds both halves
- **WHEN** the API or web service is started via the `demo` profile
- **THEN** `docker-compose.yml` builds the API from the `backend/` context and the web service from the `frontend/` context

#### Scenario: DevContainer mounts the repository root
- **WHEN** the DevContainer is opened
- **THEN** the repository root is bind-mounted as the workspace and both `backend/` and `frontend/` are visible and editable inside the container