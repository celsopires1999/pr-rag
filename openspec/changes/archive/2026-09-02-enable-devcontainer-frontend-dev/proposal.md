## Why

The devcontainer image is .NET-only. The `web/` front-end (Vite + React + TypeScript) requires Node.js, which is not installed in the container. Developers using the devcontainer cannot run `npm run dev`, `npm run build`, or any npm command for the front-end without installing Node.js on the host or switching to the host terminal.

## What Changes

- Add Node.js (LTS) to the `.devcontainer/Dockerfile` so `npm` is available inside the container.
- Add recommended VS Code extensions for front-end development (ESLint/Tailwind/intellisense) to `devcontainer.json`.
- No breaking changes. Existing .NET workflow is unaffected.

## Capabilities

### New Capabilities
- `devcontainer-frontend-tooling`: Node.js and npm availability inside the devcontainer, plus front-end VS Code extensions.

### Modified Capabilities
- `devcontainer-environment`: The devcontainer now includes Node.js alongside the .NET SDK. The spec requirements for tooling availability expand to cover front-end development.

## Impact

- `.devcontainer/Dockerfile` — add Node.js installation step.
- `.devcontainer/devcontainer.json` — add front-end VS Code extensions to the recommendations list.
- Dev image build time increases slightly (Node.js layer).
- No impact on runtime containers (`api`, `web`, `datagen`).
