# DevContainer Environment

## Purpose

Provide a reproducible development environment for the .NET 10 API via a DevContainer, so developers can build, run, test, and debug against the pgvector database without installing `dotnet` on the host machine.

## Requirements

### Requirement: Reproducible DevContainer workspace
The system SHALL provide a DevContainer configuration that starts a .NET 10 development workspace with the SDK available inside the container and a persistent place for the project source.

#### Scenario: Open project in a container
- **WHEN** a developer opens the repository in VS Code / Codespaces using the DevContainer
- **THEN** a .NET 10 SDK development container starts with the project source mounted and its working directory set to the repository root

#### Scenario: SDK available without host install
- **WHEN** the DevContainer is running
- **THEN** the `dotnet` CLI (SDK) is available inside the container without requiring a `dotnet` installation on the host machine

### Requirement: Dev database integration
The development environment SHALL reuse the existing `pgvector` database service so the running app can connect and run migrations against it.

#### Scenario: Database service available to the dev container
- **WHEN** the DevContainer starts
- **THEN** it connects to the same PostgreSQL/pgvector service used by the application, and the app developer can reach it when launching the API locally in the container

### Requirement: Recommended extensions auto-installed
The development environment SHALL define and auto-install the recommended VS Code extensions (C#, Docker, PostgreSQL, etc.) when the DevContainer is opened.

#### Scenario: Extensions installed on startup
- **WHEN** a developer opens the DevContainer
- **THEN** the recommended extensions are installed automatically into the container

#### Scenario: Recommended extensions discoverable
- **WHEN** a developer opens the repository outside the container (or inspects the workspace)
- **THEN** the recommended extension list is discoverable via the workspace extension recommendations

### Requirement: Pre-configured build and test tasks
The development environment SHALL provide pre-configured VS Code tasks to build, watch, and test the solution from inside the container.

#### Scenario: Build/restore task available
- **WHEN** a developer runs the build task
- **THEN** the solution restores and builds using the in-container SDK

#### Scenario: Test task available
- **WHEN** a developer runs the test task
- **THEN** integration/unit tests execute using the in-container SDK and the connected database service

### Requirement: Pre-configured debug launch
The development environment SHALL provide a ready-to-use debug configuration (launch) for the API that starts without `dotnet` on the host.

#### Scenario: Start debugging with a single action
- **WHEN** a developer starts a debugging session (e.g., F5)
- **THEN** the `PrRag.Api` starts with breakpoints enabled, reachable for iteration, and connected to the dev database service

### Requirement: Non-blocking container start
The DevContainer SHALL start the workspace without waiting for downstream services to become ready, so that opening the environment does not appear to hang.

#### Scenario: Startup does not wait on the database healthcheck
- **WHEN** a developer opens the DevContainer
- **THEN** the workspace container starts immediately without blocking on the `db` service healthcheck, while the database service remains available for running the API

### Requirement: Secrets not exposed to the container environment
The DevContainer SHALL NOT inject API keys into the workspace container's environment, where they could be printed by Docker/Dev Containers tooling (e.g., `docker compose config` or extension logs).

#### Scenario: API key absent from the devcontainer service environment
- **WHEN** the DevContainer configuration is inspected (e.g., `docker compose config` or Dev Containers logs)
- **THEN** the OpenAI API key is not present in the `devcontainer` service environment, and the debug runtime reads it from `.env` via `envFile`

### Requirement: Source mounted as a volume, not copied
The DevContainer SHALL obtain the project source via a bind-mounted volume (synchronized with the host), never by copying files into the image. Copying source is reserved for production/runtime images.

#### Scenario: Repo bind-mounted at the workspace root
- **WHEN** the DevContainer starts
- **THEN** the repository root is bind-mounted at the workspace root (`workspaceFolder`), so edits made on the host are immediately reflected in the container and vice-versa

#### Scenario: Dev image does not copy project source
- **WHEN** the DevContainer image is built
- **THEN** the `.devcontainer/Dockerfile` does not `COPY` the solution or any project files into the image (source comes from the bind mount), and there is no `dotnet restore` during the dev image build
