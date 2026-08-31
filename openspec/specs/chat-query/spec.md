# Chat Query

## Purpose

Natural-language Q&A over purchase requisitions, answering questions grounded in context retrieved via vector similarity search over embeddings using the configured embedding and chat models.

## Requirements

### Requirement: Chat over purchase requisitions
The system SHALL accept a natural-language question and return an answer grounded in purchased-requisition context retrieved via vector similarity search over embeddings, using the configured embedding and chat models. When the exact code-based search does not fill the configured retrieval depth, the system SHALL rewrite the question into an optimized English query before embedding, and SHALL use the original question in the final answer prompt.

#### Scenario: Successful grounded answer
- **WHEN** a client sends `POST /api/chat` with a question and valid `top_k`/`min_similarity`
- **THEN** the system acquires the rewritten query, embeds the rewritten query, retrieves the top relevant requisitions above the similarity threshold, and returns an OpenAI-generated answer citing the retrieved context

#### Scenario: Configurable retrieval depth
- **WHEN** the client provides a `top_k` value
- **THEN** the system retrieves at most that many requisitions for context

#### Scenario: Configurable relevance threshold
- **WHEN** the client provides a `min_similarity` value
- **THEN** the system discards retrieved requisitions with similarity below that threshold

#### Scenario: No relevant context found
- **WHEN** the vector search returns no requisitions meeting the similarity threshold for the rewritten question
- **THEN** the system responds with a message stating it does not have enough information in the purchase requisitions to answer

#### Scenario: Rewriter runs only when vector search is needed
- **WHEN** the exact code-based search already returns a number of requisitions equal to or greater than `top_k`
- **THEN** the system skips the query rewriter and vector search

#### Scenario: Original question used for final answer
- **WHEN** a vector search is performed using a rewritten query
- **THEN** the system still builds the final answer prompt using the original user question

### Requirement: RAG controls configured via environment
The system SHALL expose the embedding model, chat model, and default retrieval parameters through `IConfiguration`/environment, and SHALL use environment-provided API key without committing it.

#### Scenario: Default control values
- **WHEN** the client omits `top_k` and `min_similarity`
- **THEN** the system uses the configured defaults for both parameters

#### Scenario: API key supplied at runtime
- **WHEN** the system starts with an API key provided via environment
- **THEN** it authenticates against the OpenAI API without the key appearing in the repository

### Requirement: Non-streaming chat responses
The system SHALL return chat answers as a complete response rather than streaming tokens.

#### Scenario: Full response returned
- **WHEN** a client calls `POST /api/chat`
- **THEN** the system returns the complete answer in a single response body

### Requirement: Chat answers emit observability records
The chat answer pipeline SHALL additionally emit an observability trace for each answered question, without changing the answer content or the public `POST /api/chat` response body.

#### Scenario: Answer pipeline emits a trace
- **WHEN** `POST /api/chat` is answered
- **THEN** the pipeline records the question-to-answer trace for observability while returning the unchanged answer in the response body

#### Scenario: Public response contract unchanged
- **WHEN** a client sends `POST /api/chat`
- **THEN** the response body remains identical to the behavior without observability, and report generation has no effect on the response
