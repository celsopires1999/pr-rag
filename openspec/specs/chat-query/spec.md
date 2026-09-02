# Chat Query

## Purpose

Natural-language Q&A over purchase requisitions, answering questions grounded in context retrieved via vector similarity search over embeddings using the configured embedding and chat models.

## Requirements

### Requirement: Chat over purchase requisitions
The system SHALL accept a natural-language question and return an answer grounded in purchase-requisition context retrieved by the chat model via tool calls against PostgreSQL, using the configured embedding and chat models. The model SHALL decide whether to invoke a retrieval tool and which one, rather than the pipeline forcing retrieval on every question.

#### Scenario: Successful grounded answer
- **WHEN** a client sends `POST /api/chat` with a question and valid `top_k`/`min_similarity`
- **THEN** the system lets the chat model choose whether to call a PostgreSQL retrieval tool, resolves any tool results, and returns an OpenAI-generated answer grounded in the retrieved context

#### Scenario: Exact lookup via tool
- **WHEN** the model calls the exact-match lookup tool for `ITM-*`/`SUP*` codes
- **THEN** the system returns the matching requisitions to the model and grounds the answer on them

#### Scenario: Semantic search via tool
- **WHEN** the model calls the semantic search tool
- **THEN** the system embeds the search text and returns the requisitions above the similarity threshold to the model

#### Scenario: Configurable retrieval depth
- **WHEN** the client provides a `top_k` value
- **THEN** the semantic search tool retrieves at most that many requisitions for context

#### Scenario: Configurable relevance threshold
- **WHEN** the client provides a `min_similarity` value
- **THEN** the system discards retrieved requisitions with similarity below that threshold

#### Scenario: No relevant context found
- **WHEN** the model performs retrieval that returns no requisitions meeting the requirements for the question
- **THEN** the system responds with a message stating it does not have enough information in the purchase requisitions to answer

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

### Requirement: Non-streaming endpoint remains the compatibility path
The `POST /api/chat` endpoint SHALL continue to return complete, non-streaming responses as the compatibility path, with streaming available via a separate endpoint.

#### Scenario: Non-streaming endpoint unaffected
- **WHEN** a client calls `POST /api/chat`
- **THEN** the endpoint returns the complete answer in a single response body as before
