# Query Rewriter

## Purpose

Transforms raw user questions into optimized queries for vector similarity search.

## Requirements

### Requirement: Rewrite question for semantic retrieval
The system SHALL provide a query rewriter that transforms a raw user question into a short, keyword-rich, English query optimized for cosine similarity search against the indexed requisition fields, using an LLM call.

#### Scenario: Rewrites noisy Portuguese question
- **WHEN** the rewriter receives a Portuguese question containing slang and filler (e.g. "opa me diz quais PRs tem pra aquela bomba hidráulica da Acme")
- **THEN** it returns a concise English query focused on the entity names and concepts (e.g. "hydraulic pump Acme")

#### Scenario: Returns only the optimized query
- **WHEN** the rewriter processes a question
- **THEN** it returns only the optimized query string with no explanations or field labels

#### Scenario: Preserves a pure code query
- **WHEN** the question consists solely of a code (e.g. "ITM-00000000000000000001")
- **THEN** the rewriter returns the code unchanged

### Requirement: Rewrite using full conversation context
The system SHALL rewrite the semantic search query using the full conversation history, so the rewriter can resolve references (e.g. "that one", "the other", "as we saw earlier") against prior turns and incorporate the resolved entities into the optimized query.

#### Scenario: Resolves references using prior turns
- **WHEN** a follow-up question refers to an entity mentioned in an earlier conversation turn (e.g. a user first asks about Acme and then asks "and that other one we saw before")
- **THEN** the rewriter produces a query containing the resolved entity from the earlier turn instead of the bare reference

#### Scenario: Rewriter receives the whole conversation
- **WHEN** the semantic search tool invokes the rewriter
- **THEN** the rewriter receives the entire conversation history, not only the isolated search query

### Requirement: Rewriter failure surfaces as error
The system SHALL propagate a query-rewriter failure as a request error rather than silently falling back to the raw question.

#### Scenario: Rewriter call fails
- **WHEN** the rewriter LLM call times out or returns an error
- **THEN** the request fails and reports the error

### Requirement: Rewriter implementation lives in the Application layer
The system SHALL implement the query rewriter as provider-agnostic logic within the Application layer, consuming an `IChatClient` abstraction rather than an OpenAI-specific SDK, so the Application owns the pure LLM-backed behavior.

#### Scenario: Rewriter resolves from the Application layer
- **WHEN** the service container resolves an `IQueryRewriter`
- **THEN** it resolves the concrete `SemanticQueryRewriter` implementation registered by the Application layer

#### Scenario: Rewriter uses a provider-agnostic client
- **WHEN** the rewriter performs its rewrite call
- **THEN** it does so through the `IChatClient` abstraction, with no direct dependency on the OpenAI SDK
