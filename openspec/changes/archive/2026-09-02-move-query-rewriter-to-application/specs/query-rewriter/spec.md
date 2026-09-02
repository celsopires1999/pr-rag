## ADDED Requirements

### Requirement: Rewriter implementation lives in the Application layer
The system SHALL implement the query rewriter as provider-agnostic logic within the Application layer, consuming an `IChatClient` abstraction rather than an OpenAI-specific SDK, so the Application owns the pure LLM-backed behavior.

#### Scenario: Rewriter resolves from the Application layer
- **WHEN** the service container resolves an `IQueryRewriter`
- **THEN** it resolves the concrete `SemanticQueryRewriter` implementation registered by the Application layer

#### Scenario: Rewriter uses a provider-agnostic client
- **WHEN** the rewriter performs its rewrite call
- **THEN** it does so through the `IChatClient` abstraction, with no direct dependency on the OpenAI SDK
