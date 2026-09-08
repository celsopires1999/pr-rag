## MODIFIED Requirements

### Requirement: Tool definitions match retrieval semantics
The four available tools SHALL be defined as `[Description]`-annotated methods in a dedicated tools class and registered on the composed MAF agent via `AIFunctionFactory`, mirroring the existing repository retrieval methods, so a tool call maps to a single PostgreSQL query.

#### Scenario: Exact-match maps to code search
- **WHEN** the model calls the exact-match lookup tool
- **THEN** the system executes the code-based search over item/supplier codes

#### Scenario: Semantic search maps to vector query
- **WHEN** the model calls the semantic search tool
- **THEN** the system executes the embedding-based vector similarity query