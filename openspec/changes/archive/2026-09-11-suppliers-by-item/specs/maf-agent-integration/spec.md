## MODIFIED Requirements

### Requirement: MAF tool registration
The system SHALL register the five existing tool functions (`search_by_codes`, `search_semantic`, `activate_skill`, `create_requisition`, `get_suppliers_by_item`) with the composed agent via its `Tools` property, using `AIFunctionFactory.Create` on `[Description]`-annotated methods defined in a dedicated tools class rather than delegates inlined in `ChatService`.

#### Scenario: Tools bound to agent
- **WHEN** the agent is composed in DI
- **THEN** it exposes the five tool functions so that `RunAsync`/`RunStreamingAsync` can invoke them during the agentic reasoning loop

#### Scenario: Tool implementations unchanged
- **WHEN** a tool is invoked during a chat request
- **THEN** the same repository, embedding, skill, and requisition-writer methods are called with the same parameters and return the same results as before, including per-turn `top_k`/`min_similarity` and retrieval bookkeeping for the observability report

#### Scenario: Item supplier lookup tool registered
- **WHEN** the agent is composed in DI
- **THEN** `get_suppliers_by_item` is among the registered tools and is callable by the agent to return the distinct suppliers for an item