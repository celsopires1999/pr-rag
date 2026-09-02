## MODIFIED Requirements

### Requirement: Recommended extensions auto-installed
The development environment SHALL define and auto-install the recommended VS Code extensions (C#, Docker, PostgreSQL, Tailwind CSS, ESLint/oxlint, etc.) when the DevContainer is opened.

#### Scenario: Extensions installed on startup
- **WHEN** a developer opens the DevContainer
- **THEN** the recommended extensions are installed automatically into the container

#### Scenario: Recommended extensions discoverable
- **WHEN** a developer opens the repository outside the container (or inspects the workspace)
- **THEN** the recommended extension list is discoverable via the workspace extension recommendations
