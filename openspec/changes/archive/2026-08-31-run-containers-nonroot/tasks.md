## 1. API image (Dockerfile)

- [x] 1.1 Add a non-root user creation to the runtime stage of `Dockerfile` (e.g. `useradd`/`adduser` for the `app` user with UID 1000) and `chown` `/app` so the published output is readable
- [x] 1.2 Set `USER <non-root-user>` before `ENTRYPOINT` so the ASP.NET Core process runs unprivileged
- [x] 1.3 Ensure `WORKDIR /app` and any runtime-writable dirs (`/app/reports`) are owned/writable by the non-root user

## 2. Web image (Dockerfile.web + nginx)

- [x] 2.1 Add an nginx config that listens on a non-privileged port (e.g. 8080) instead of 80
- [x] 2.2 Copy the built static assets into `/usr/share/nginx/html` (or a user-accessible path) and set their ownership so the `nginx` user can read them
- [x] 2.3 Set `USER nginx` (or the nginx UID) in `Dockerfile.web` runtime stage and expose the new port

## 3. Data generator image (Dockerfile.datagen)

- [x] 3.1 Add a non-root user to the runtime stage of `Dockerfile.datagen` and `chown` `/app`
- [x] 3.2 Set `USER` to the non-root user; the container mounts `./data` at `/data`

## 4. Integration tests image (Dockerfile.test)

- [x] 4.1 Add a non-root user to the runtime stage of `Dockerfile.test` and `chown` the `/src` working tree so `dotnet test` can write artifacts
- [x] 4.2 Set `USER` to the non-root user before running `dotnet test`

## 5. Compose orchestration

- [x] 5.1 Update `docker-compose.yml` `web` service port mapping to the new non-privileged container port (was `:80`)
- [x] 5.2 Ensure the `db` service keeps/confirms the official Postgres non-root `postgres` user (no root override)
- [x] 5.3 Ensure the DevContainer keeps its non-root `vscode` user incl. docker.sock access; confirm no `user: root` overrides in compose
- [x] 5.4 Document that host `./data` and `./reports` must be writable by the container non-root users (chown/chmod requirement), and update README/examples if needed

## 6. Verification

- [x] 6.1 Build all images (`docker compose --profile demo up -d --build api web`, `datagen`, test) without errors
- [x] 6.2 Assert each running service's primary PID runs with a non-zero UID (e.g. `docker compose ps -q | xargs ... exec ... id -u`) and is non-root
- [x] 6.3 Verify the API can write a RAG observability report to `/app/reports` and the datagen can write to `/data` as the non-root user
- [x] 6.4 Run `dotnet build PrRag.sln` and `dotnet test tests/PrRag.Tests` (or the compose test profile) to confirm nothing regressed
