# Agentic Retrieval

## Purpose

Decide whether and what context to retrieve from PostgreSQL by letting the chat model call tool functions, rather than running a fixed retrieval pipeline on every question.

## Requirements

### Requirement: Tool-selected PostgreSQL retrieval
The system SHALL expose the PostgreSQL lookup operations as tool functions the chat model may call during a conversation, and SHALL let the model decide whether to invoke them and which one to use.

#### Scenario: Model calls the exact-match lookup tool
- **WHEN** the model determines a question references exact codes (for example `ITM-*` items or `SUP*` suppliers)
- **THEN** the system executes the exact-match PostgreSQL lookup against the requisition codes and returns the matching requisitions to the model

#### Scenario: Model calls the semantic search tool
- **WHEN** the model determines a question is best answered by semantic similarity
- **THEN** the system embeds the search text, runs a vector similarity search over requisition embeddings, and returns the ranked results above the configured similarity threshold to the model

#### Scenario: Model answers without retrieval
- **WHEN** the model decides a question does not require PostgreSQL context
- **THEN** the system answers from the conversation alone without invoking any tool

### Requirement: Tool definitions match retrieval semantics
The two available tools SHALL mirror the existing repository retrieval methods, so a tool call maps to a single PostgreSQL query.

#### Scenario: Exact-match maps to code search
- **WHEN** the model calls the exact-match lookup tool
- **THEN** the system executes the code-based search over item/supplier codes

#### Scenario: Semantic search maps to vector query
- **WHEN** the model calls the semantic search tool
- **THEN** the system executes the embedding-based vector similarity query

### Requirement: Answer grounded in retrieved context
The system SHALL build the final answer using the requisition context that the model actually retrieved through its tool calls, rather than unrelated context.

#### Scenario: Answer cites retrieved requisitions
- **WHEN** the model invokes one or more retrieval tools and then answers
- **THEN** the system returns an answer grounded in the requisitions returned by those tool calls

### Requirement: Retrieval parameters configurable
The system SHALL keep `top_k` and `min_similarity` configurable and SHALL apply them to tool-driven retrieval, with environment-provided defaults when the client omits them.

#### Scenario: Client overrides retrieval depth
- **WHEN** the client provides a `top_k` value
- **THEN** the semantic search tool retrieves at most that many requisitions

#### Scenario: Defaults from configuration
- **WHEN** the client omits `top_k` and `min_similarity`
- **THEN** the system uses the configured defaults for tool-driven retrieval

### Requirement: No-context graceful answer
The system SHALL answer gracefully when the model performs no successful retrieval that can ground the answer.

#### Scenario: Answer without usable context
- **WHEN** the model produces an answer without retrieved requisition context
- **THEN** the system streams/returns that answer as usual, indicating it relied on available conversation context
