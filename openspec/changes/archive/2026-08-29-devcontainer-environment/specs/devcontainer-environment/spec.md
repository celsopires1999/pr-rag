## ADDED Requirements

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
