## MODIFIED Requirements

### Requirement: Tool-selected PostgreSQL retrieval
The system SHALL expose the PostgreSQL lookup operations as tool functions the chat model may call during a conversation, and SHALL let the model decide whether to invoke them and which one to use.

#### Scenario: Model calls the exact-match lookup tool
- **WHEN** the model determines a question references exact codes (for example `ITM-*` items or `SUP*` suppliers)
- **THEN** the system executes the exact-match PostgreSQL lookup against the requisition codes and returns the matching requisitions to the model

#### Scenario: Model calls the semantic search tool
- **WHEN** the model determines a question is best answered by semantic similarity
- **THEN** the model rewrites the search text using the full conversation context per the system prompt, and the system embeds the model-provided query, runs a vector similarity search over requisition embeddings, and returns the ranked results above the configured similarity threshold to the model

#### Scenario: Semantic search embeds the model's query directly
- **WHEN** the model calls the semantic search tool with a query it has already rewritten
- **THEN** the system embeds that query as-is without invoking a separate query rewriter

#### Scenario: Exact-match tool performs code search only
- **WHEN** the model calls the exact-match lookup tool
- **THEN** the system executes the code-based search without running a semantic embedding or vector query

#### Scenario: Model answers without retrieval
- **WHEN** the model decides a question does not require PostgreSQL context
- **THEN** the system answers from the conversation alone without invoking any tool