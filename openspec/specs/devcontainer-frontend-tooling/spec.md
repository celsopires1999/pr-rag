# DevContainer Frontend Tooling

## Purpose

Provide the front-end development tooling (Node.js, npm, and VS Code extensions) inside the DevContainer so developers can build, run, lint, and iterate on the React front-end without installing Node.js on the host machine.

## Requirements

### Requirement: Node.js and npm available in the devcontainer
The devcontainer SHALL include Node.js (LTS) and npm so that front-end development commands (`npm install`, `npm run dev`, `npm run build`, `npm run lint`) work inside the container without host-side Node.js.

#### Scenario: Run npm commands inside the devcontainer
- **WHEN** a developer opens a terminal in the devcontainer
- **THEN** `node --version` and `npm --version` return valid versions

#### Scenario: Install front-end dependencies
- **WHEN** a developer runs `npm install` in the `web/` directory inside the devcontainer
- **THEN** `node_modules` is created and all dependencies resolve without errors

#### Scenario: Run Vite dev server
- **WHEN** a developer runs `npm run dev` in the `web/` directory inside the devcontainer
- **THEN** the Vite dev server starts and is accessible on the expected port

### Requirement: Front-end VS Code extensions installed
The devcontainer SHALL auto-install VS Code extensions relevant to the front-end stack (Tailwind CSS IntelliSense, ESLint/oxlint support) alongside the existing .NET extensions.

#### Scenario: Tailwind CSS IntelliSense available
- **WHEN** a developer opens a `.css` or Tailwind-aware file in the devcontainer
- **THEN** Tailwind CSS class IntelliSense and hover information are available

#### Scenario: Linting support available
- **WHEN** a developer opens a TypeScript/JavaScript file in the devcontainer
- **THEN** oxlint/ESLint diagnostics are available via the VS Code extension
