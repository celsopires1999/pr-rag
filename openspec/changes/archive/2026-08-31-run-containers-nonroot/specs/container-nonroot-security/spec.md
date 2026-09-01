## ADDED Requirements

### Requirement: Containers run primary processes as non-root
Every service container image used by the application SHALL run its primary workload process as an unprivileged, non-root user rather than as `root`.

#### Scenario: API runs as non-root
- **WHEN** the `api` container starts
- **THEN** the ASP.NET Core process runs as an unprivileged user with a non-zero UID and no root privileges

#### Scenario: Web server runs as non-root
- **WHEN** the `web` container starts
- **THEN** the nginx process runs as an unprivileged user on a port above 1024, with read access to the served static assets

#### Scenario: Data generator runs as non-root
- **WHEN** the `datagen` container starts
- **THEN** the generator process runs as an unprivileged user with write access to the bind-mounted data directory

#### Scenario: Integration tests run as non-root
- **WHEN** the `test` container starts and runs `dotnet test`
- **THEN** the test process runs as an unprivileged user with write access to its working/artifact directories

#### Scenario: DevContainer user is non-root
- **WHEN** the DevContainer starts
- **THEN** the workspace user is a non-root user and the container does not elevate to root for the primary workflow

#### Scenario: Database user is non-root
- **WHEN** the `db` container starts from the official Postgres image
- **THEN** the Postgres server process runs as the image's default non-root `postgres` user and is not overridden to run as root

### Requirement: Writable bind mounts remain functional
The containers' writable bind mounts SHALL remain writable by the unprivileged users introduced for the API, data generator, and test containers, while read-only mounts remain readable.

#### Scenario: API reports directory writable
- **WHEN** the `api` container writes a RAG observability report
- **THEN** it succeeds writing to `/app/reports` without requiring root privileges

#### Scenario: Data generator can write dataset
- **WHEN** the `datagen` container writes the dataset
- **THEN** it succeeds writing to the bind-mounted data directory without requiring root privileges

#### Scenario: Confirmed running without root PID
- **WHEN** any service container is running
- **THEN** its primary process has a non-zero UID and the container does not report root as its primary user
