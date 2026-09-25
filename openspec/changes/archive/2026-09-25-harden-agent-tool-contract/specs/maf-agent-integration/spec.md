## ADDED Requirements

### Requirement: Typed tool result contracts
The system SHALL return tool results as dedicated result DTOs whose properties carry explicit camelCase JSON names, and SHALL let the AI function layer JSON-serialize those DTOs rather than passing raw domain, report, or repository objects through to the chat model. Result DTOs SHALL be decoupled from the RAG observability report schema so that report consumers are unaffected by changes to the model-facing payload.

#### Scenario: Search tool returns a counted result object
- **WHEN** the chat model calls `search_by_codes` or `search_semantic`
- **THEN** the tool result is a JSON object containing a result count and an items array of requisition hits with camelCase keys, rather than a bare array of framework objects

#### Scenario: Supplier lookup returns a counted result object
- **WHEN** the chat model calls `get_suppliers_by_item`
- **THEN** the tool result is a JSON object containing a supplier count and a suppliers array of `{supplierCode, supplierName}` pairs

#### Scenario: Skill activation returns a structured result
- **WHEN** the chat model calls `activate_skill`
- **THEN** the tool result is a JSON object carrying the requested skill name, an explicit found flag, the guidance body when the skill exists, and a human-readable message in both the found and not-found cases

#### Scenario: Requisition creation returns a structured result
- **WHEN** the chat model calls `create_requisition`
- **THEN** the tool result is a JSON object carrying an explicit success flag, the created requisition id when the write succeeded, and a human-readable message describing the outcome

#### Scenario: Empty results are unambiguous
- **WHEN** a search or lookup tool finds no matching records
- **THEN** the result object reports a count of zero with an empty array, so the model can distinguish "no matches" from a malformed response

#### Scenario: Observability report schema is unaffected
- **WHEN** the RAG observability report is written for a turn whose tools returned the new typed results
- **THEN** the report's retrieved-item and tool-call fields keep their existing shape and property naming

## MODIFIED Requirements

### Requirement: MAF tool registration
The system SHALL register the five existing tool functions (`search_by_codes`, `search_semantic`, `activate_skill`, `create_requisition`, `get_suppliers_by_item`) with the composed agent via its `Tools` property, using `AIFunctionFactory.Create` on `[Description]`-annotated methods defined in a dedicated tools class rather than delegates inlined in `ChatService`. Each method SHALL be registered directly, not through a forwarding lambda.

#### Scenario: Tools bound to agent
- **WHEN** the agent is composed in DI
- **THEN** it exposes the five tool functions so that `RunAsync`/`RunStreamingAsync` can invoke them during the agentic reasoning loop

#### Scenario: Tool implementations unchanged
- **WHEN** a tool is invoked during a chat request
- **THEN** the same repository, embedding, skill, and requisition-writer methods are called with the same parameters and return the same underlying results as before, including per-turn `top_k`/`min_similarity` and retrieval bookkeeping for the observability report

#### Scenario: Item supplier lookup tool registered
- **WHEN** the agent is composed in DI
- **THEN** `get_suppliers_by_item` is among the registered tools and is callable by the agent to return the distinct suppliers for an item
