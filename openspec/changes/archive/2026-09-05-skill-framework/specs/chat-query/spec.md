## MODIFIED Requirements

### Requirement: Chat over purchase requisitions
The system SHALL accept a natural-language question and return an answer grounded in purchase-requisition context retrieved by the chat model via tool calls against PostgreSQL, using the configured embedding and chat models. The model SHALL decide whether to invoke a retrieval tool and which one, rather than the pipeline forcing retrieval on every question. When a request matches a registered skill, the system SHALL additionally engage the skill's guided workflow instead of answering in free-form; when no skill matches, behavior is unchanged.

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

#### Scenario: Skill-guided request matches a registered skill
- **WHEN** a user request matches a registered skill (for example asking to create a purchase requisition)
- **THEN** the system activates the matching skill and the assistant guides the user through the skill's workflow instead of answering in free-form

#### Scenario: Plain question with no matching skill
- **WHEN** a user request does not match any registered skill
- **THEN** the system answers in free-form exactly as before, with no skill guidance involved