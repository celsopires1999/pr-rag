## Why

All application containers currently run their processes as `root`, which is a security risk: a compromise of any workload has full privileges inside the container and the host kernel as seen by that container. Running as an unprivileged user is a container-security best practice that limits the blast radius of a breakout.

## What Changes

- **API** (`Dockerfile`): run the .NET runtime as a dedicated, unprivileged user instead of `root`, with the needed permission on the `/app/reports` bind mount kept working.
- **Web** (`Dockerfile.web`): run nginx as a non-root user, using a non-privileged port and keeping the web root readable.
- **Data generator** (`Dockerfile.datagen`): run the generator as a non-root user with write permission on the `/data` bind mount.
- **Tests** (`Dockerfile.test`): run `dotnet test` as a non-root user; ensure the test runner can write its artifacts under `/src`.
- **DevContainer** (`.devcontainer/Dockerfile` + config): ensure the dev user (non-root `vscode`) is used for the workspace, including the docker.sock mount behavior.
- **Database** (`docker-compose.yml` `db`): confirm and pin the official Postgres image's default non-root `postgres` user rather than overriding to root — no root override remains.
- No application library code changes; this is container-image and orchestration configuration only.

## Capabilities

### New Capabilities
- `container-nonroot-security`: the container images and compose orchestration run their primary service processes as unprivileged, non-root users rather than `root`.

### Modified Capabilities
<!-- No existing spec-level behavior changes. The devcontainer still starts a working .NET workspace; only its privilege model changes, covered by the new capability. -->

## Impact

- `Dockerfile`, `Dockerfile.web`, `Dockerfile.datagen`, `Dockerfile.test`, `.devcontainer/Dockerfile`, `.devcontainer/devcontainer.json`.
- `docker-compose.yml` and `docker-compose.test.yml` (volume permissions, any `user:` pins, and the non-root ports).
- Runtime behavior: bind mounts must remain writable/readable by the unprivileged user; the API's `reports` output and the datagen `data` output directories are the two writable mounts.
- Verification: `dotnet build`/`dotnet test` unchanged; every service started via compose must show a non-root primary PID.
