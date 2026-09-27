## MODIFIED Requirements

### Requirement: RAG controls configured via environment
The system SHALL expose the embedding model, chat model, and default retrieval parameters through `IConfiguration`/environment, and SHALL use an environment-provided API key without committing it. The system SHALL NOT name a single vendor in this requirement: the same environment-provided key SHALL be used to authenticate against whichever provider the system is configured to use, and the models SHALL be resolved through that provider rather than against a fixed vendor's catalogue.

#### Scenario: Default control values
- **WHEN** the client omits `top_k` and `min_similarity`
- **THEN** the system uses the configured defaults for both parameters

#### Scenario: API key supplied at runtime
- **WHEN** the system starts with an API key provided via environment
- **THEN** it authenticates against the configured provider without the key appearing in the repository, and the requirement holds for every supported provider rather than for one vendor
