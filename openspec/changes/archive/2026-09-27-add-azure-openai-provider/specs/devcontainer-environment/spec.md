## MODIFIED Requirements

### Requirement: Secrets not exposed to the container environment
The DevContainer SHALL NOT inject any provider credential into the workspace container's environment, where they could be printed by Docker/Dev Containers tooling (e.g., `docker compose config` or extension logs). This covers the credential of every supported provider, not only one: the set of credentials that could be present grows with the number of providers, and an invariant written against a single credential silently stops covering the others.

#### Scenario: Provider credentials absent from the devcontainer service environment
- **WHEN** the DevContainer configuration is inspected (e.g., `docker compose config` or Dev Containers logs)
- **THEN** no provider API key is present in the `devcontainer` service environment, for any supported provider, and the debug runtime reads it from `.env` via `envFile`
