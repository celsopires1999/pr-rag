## Context

All of this repo's runtime containers currently start their primary process as `root`:

- `api` — `mcr.microsoft.com/dotnet/aspnet:10.0` (root by default)
- `web` — `nginx:1.27-alpine` (root master, worker as `nginx`)
- `datagen` — `mcr.microsoft.com/dotnet/runtime:10.0` (root by default)
- `test` — `mcr.microsoft.com/dotnet/sdk:10.0` (root by default)
- `devcontainer` — `devcontainers/dotnet:1-10.0` (runs as non-root `vscode` user already)
- `db` — `pgvector/pgvector:pg18` (runs as non-root `postgres` user already)

Two writable bind mounts are the main complication: the API writes RAG observability reports to `./reports` (mounted at `/app/reports`), and the datagen writes to `./data` (mounted at `/data`). A non-root user must have write access to those host directories.

There are no application library changes; this is purely container-image and compose configuration.

## Goals / Non-Goals

**Goals:**
- Every service container runs its primary process under a non-root, non-zero UID.
- Writable bind mounts (API reports, datagen data, test artifacts) stay writable by the unprivileged user.
- Preserve current behavior and the existing `dotnet build` / `dotnet test` verification.
- Keep the DevContainer's existing non-root `vscode` user incl. docker.sock access.

**Non-Goals:**
- Running at a container-runtime level (Kubernetes SecurityContext, PodSecurity, etc.) — this repo is compose-only.
- Applying SELinux/AppArmor profiles, seccomp tightening, or read-only root filesystems — those are separate hardening steps.
- Changing application code or the RAG/chat domain behavior.

## Decisions

- **Use a dedicated non-root user in each Dockerfile runtime stage.**
  For .NET images, add a `useradd` (or use `USER 1000:1000`) in the runtime stage and `chown` the working/data dirs. This is the minimal, image-native approach.
  *Alternative considered:* a generic numeric UID via `USER $APP_UID` — rejected because a named user is clearer and still maps to a fixed UID.

- **Web/nginx: pin to non-privileged port and `USER` nginx.**
  Append `RUN ... ` to listen on a port above 1024 (e.g. 8080) in the nginx config and run with `USER nginx` (or `101`), copying the build output with correct ownership. The compose `web` port mapping changes from `:80` to the new container port.
  *Alternative considered:* keep port 80 and use `setcap` — rejected; running on a high port with a plain user is simpler and avoids capability complexity.

- **Writable bind mounts solved via host directory ownership.**
  Compose `user:` is avoided; instead the images' non-root user UID must match the host-mounted directory ownership. Instruct/verify `./reports` and `./data` to be owned writable by the container user (e.g. chmod/chown by the operator, or make the directories world-writable where acceptable). Document this requirement rather than embedding root-owned-your-mount self-fixes.

- **Database and DevContainer already non-root — verify and pin.**
  Confirm `pgvector/pgvector:pg18` runs the server as `postgres` and the DevContainer as `vscode`; add tests asserting non-root primary PID across all compose services.

## Risks / Trade-offs

- [Host mount permission mismatches for `reports`/`data`] → Document required host ownership; verification asserts the service starts and writes successfully.
- [nginx high-port change may surprise consumers depending on `:80`] → It's compose-internal; ports are already configurable via env. Update README/examples.
- [`.NET` image lacks `useradd` on some tagged runtimes] → aspnet/runtime images include an apt toolchain sufficient for `useradd`; use a numeric UID fallback if needed.
- [Changing test container user could affect write permissions for test output] → chown the test working directory in the image before switching `USER`.

## Migration Plan

1. Update each Dockerfile runtime stage to create + use a non-root user and set working-dir ownership.
2. Update nginx config and `Dockerfile.web`; update `docker-compose.yml` `web` port mapping.
3. Ensure host `data/` and `reports/` are writable by the container users and record the requirement.
4. Verify: `docker compose up -d` (db) and `docker compose --profile demo up -d --build`, then assert each service's primary PID UID is non-zero; run tests.
5. Rollback: revert Dockerfile/compose changes (git) and rebuild; no data migration involved.

## Open Questions

- Which host side owns `reports/` and `data/` in production — is chown-by-operator acceptable, or should the images copy with correct ownership into named volumes instead?
- Should a named volume replace the `./reports` bind mount to sidestep host-permission management?
