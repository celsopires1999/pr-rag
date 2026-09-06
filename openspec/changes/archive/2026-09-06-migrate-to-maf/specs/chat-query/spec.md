## MODIFIED Requirements

### Requirement: Chat over purchase requisitions
The system SHALL accept a natural-language question and return an answer grounded in purchase-requisition context retrieved by the MAF `AIAgent` via tool calls against PostgreSQL, using the configured embedding and chat models. The agent SHALL decide whether to invoke a retrieval tool and which one via `RunAsync()`, rather than the pipeline forcing retrieval on every question. When a request matches a registered skill, the system SHALL additionally engage the skill's guided workflow instead of answering in free-form; when no skill matches, behavior is unchanged.

#### Scenario: Successful grounded answer
- **WHEN** a client sends `POST /api/chat` with a question, an optional `session_id`, and valid `top_k`/`min_similarity`
- **THEN** the system invokes `AIAgent.RunAsync()` on the session resolved for that `session_id`, the agent chooses whether to call a PostgreSQL retrieval tool, resolves any tool results, and returns an answer grounded in the retrieved context (with the resolved `session_id` echoed in the response)

#### Scenario: Exact lookup via tool
- **WHEN** the agent calls the exact-match lookup tool for `ITM-*`/`SUP*` codes
- **THEN** the system returns the matching requisitions to the agent and grounds the answer on them

#### Scenario: Semantic search via tool
- **WHEN** the agent calls the semantic search tool
- **THEN** the system embeds the search text and returns the requisitions above the similarity threshold to the agent

#### Scenario: Configurable retrieval depth
- **WHEN** the client provides a `top_k` value
- **THEN** the semantic search tool retrieves at most that many requisitions for context

#### Scenario: Configurable relevance threshold
- **WHEN** the client provides a `min_similarity` value
- **THEN** the system discards retrieved requisitions with similarity below that threshold

#### Scenario: No relevant context found
- **WHEN** the agent performs retrieval that returns no requisitions meeting the requirements for the question
- **THEN** the system responds with a message stating it does not have enough information in the purchase requisitions to answer

#### Scenario: Skill-guided request matches a registered skill
- **WHEN** a user request matches a registered skill (for example asking to create a purchase requisition)
- **THEN** the agent activates the matching skill and guides the user through the skill's workflow instead of answering in free-form

#### Scenario: Plain question with no matching skill
- **WHEN** a user request does not match any registered skill
- **THEN** the system answers in free-form exactly as before, with no skill guidance involved

### Requirement: RAG controls configured via environment
The system SHALL expose the embedding model, chat model, and default retrieval parameters through `IConfiguration`/environment, and SHALL use environment-provided API key without committing it.

#### Scenario: Default control values
- **WHEN** the client omits `top_k` and `min_similarity`
- **THEN** the system uses the configured defaults for both parameters

#### Scenario: API key supplied at runtime
- **WHEN** the system starts with an API key provided via environment
- **THEN** it authenticates against the OpenAI API without the key appearing in the repository

### Requirement: Non-streaming chat responses
The system SHALL return chat answers as a complete response via `AIAgent.RunAsync()` rather than streaming tokens.

#### Scenario: Full response returned
- **WHEN** a client calls `POST /api/chat`
- **THEN** the system returns the complete answer from `RunAsync()` in a single response body

### Requirement: Chat answers emit observability records
The chat answer pipeline SHALL additionally emit an observability trace for each answered question via MAF middleware, without changing the answer content or the public `POST /api/chat` response body.

#### Scenario: Answer pipeline emits a trace
- **WHEN** `POST /api/chat` is answered
- **THEN** the middleware records the question-to-answer trace for observability while returning the unchanged answer in the response body

#### Scenario: Public response contract unchanged
- **WHEN** a client sends `POST /api/chat`
- **THEN** the response body remains identical to the behavior without observability, and report generation has no effect on the response

### Requirement: Non-streaming endpoint remains the compatibility path
The `POST /api/chat` endpoint SHALL continue to return complete, non-streaming responses as the compatibility path, with streaming available via a separate endpoint.

#### Scenario: Non-streaming endpoint unaffected
- **WHEN** a client calls `POST /api/chat`
- **THEN** the endpoint returns the complete answer in a single response body as before
