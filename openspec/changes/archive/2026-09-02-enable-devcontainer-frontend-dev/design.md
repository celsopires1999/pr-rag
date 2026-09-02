## Context

The devcontainer image (`mcr.microsoft.com/devcontainers/dotnet:1-10.0`) includes only the .NET SDK. The `web/` directory contains a Vite + React + TypeScript front-end that requires Node.js and npm for local development (`npm run dev`, `npm run build`, `npm run lint`). Currently, developers using the devcontainer must either install Node.js on the host or leave the container to work on the front-end.

The front-end uses: Vite 8, React 19, TypeScript 6, Tailwind CSS v4, oxlint, and shadcn components.

## Goals / Non-Goals

**Goals:**
- Make `node` and `npm` available inside the devcontainer so front-end development works end-to-end.
- Add front-end-relevant VS Code extensions to the devcontainer recommendations.
- Keep the existing .NET workflow unchanged.

**Non-Goals:**
- Changing the runtime `web` container (Dockerfile.web / nginx setup) — that remains production-only.
- Adding Node.js to the production API or datagen images.
- Setting up hot-reload proxying between the devcontainer's Vite dev server and the browser (out of scope for this change).

## Decisions

### Use the official devcontainers Node.js feature instead of manual apt install

**Decision:** Add the Node.js feature via `devcontainer.json` `features` rather than installing Node manually in the Dockerfile.

**Rationale:** The devcontainers feature system handles version management, updates, and cross-platform compatibility automatically. It installs into the correct location for the `vscode` user and integrates with the devcontainer lifecycle. Manual apt install would require maintaining version pinning and dealing with the broken Yarn apt source workaround already in the Dockerfile.

**Alternatives considered:**
- Manual `apt-get install nodejs npm` in the Dockerfile — rejected because it conflicts with the Yarn apt source issue and doesn't integrate with devcontainer feature versioning.
- Using `nvm` in the Dockerfile — rejected because nvm is designed for interactive shell use, not image builds.

### Pin to Node.js 22 LTS

**Decision:** Use Node.js 22 (current LTS as of 2026).

**Rationale:** Node 22 is the active LTS line. It's compatible with Vite 8 and all current `web/` dependencies. Avoids surprise breakage from unversioned `node:latest`.

### Add only essential front-end VS Code extensions

**Decision:** Add `dbaeumer.vscode-eslint` (ESLint support — oxlint uses the same protocol) and `bradlc.vscode-tailwindcss` (Tailwind CSS IntelliSense) to the existing extensions list.

**Rationale:** These are the most impactful extensions for the existing stack (Tailwind v4, oxlint). Avoids bloating the extension list with redundant tools.

## Risks / Trade-offs

- **[Risk] Dev image build time increases** → Mitigation: The Node.js feature downloads a prebuilt binary; the overhead is ~15-30 seconds. Acceptable for a dev-only image.
- **[Risk] Node version drift** → Mitigation: Pin to Node 22 LTS in the feature config. Major version bumps are explicit.
- **[Trade-off] Feature install vs Dockerfile COPY** → The feature approach adds a runtime step but keeps the Dockerfile clean and maintainable. The Dockerfile remains focused on .NET + postgresql-client.
