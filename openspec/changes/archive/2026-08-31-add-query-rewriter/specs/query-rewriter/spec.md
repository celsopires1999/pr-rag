## ADDED Requirements

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

### Requirement: Rewriter failure surfaces as error
The system SHALL propagate a query-rewriter failure as a request error rather than silently falling back to the raw question.

#### Scenario: Rewriter call fails
- **WHEN** the rewriter LLM call times out or returns an error
- **THEN** the request fails and reports the error
