## MODIFIED Requirements

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
