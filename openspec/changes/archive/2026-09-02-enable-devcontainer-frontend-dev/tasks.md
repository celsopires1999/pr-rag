## 1. DevContainer configuration

- [x] 1.1 Add Node.js 22 LTS feature to `.devcontainer/devcontainer.json` `features` section
- [x] 1.2 Add `bradlc.vscode-tailwindcss` and `dbaeumer.vscode-eslint` to `customizations.vscode.extensions`

## 2. Verification

- [x] 2.1 Rebuild the devcontainer and confirm `node --version` and `npm --version` return valid output (requires container rebuild — run "Dev Containers: Rebuild Container" in VS Code)
- [x] 2.2 Run `npm install` in `web/` inside the devcontainer and confirm it succeeds (after rebuild)
- [x] 2.3 Run `npm run build` in `web/` inside the devcontainer and confirm the build completes (after rebuild)

## 3. Port exposure and host access

- [x] 3.1 Configure Vite dev server to listen on all interfaces (`server.host: true`) and port 5173 in `web/vite.config.ts`
- [x] 3.2 Publish the front-end port on the `devcontainer` service in `docker-compose.yml` (`WEB_PORT:5173`)
- [x] 3.3 Publish the API port on the `devcontainer` service in `docker-compose.yml` (`API_PORT:8080`) for host access to the API running inside the container
- [x] 3.4 Recreate the devcontainer and confirm both `5173` and `8080` are published and reachable from the host (`curl http://localhost:5173` returns 200)
