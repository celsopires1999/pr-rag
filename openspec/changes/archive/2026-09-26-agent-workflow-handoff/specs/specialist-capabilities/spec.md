## MODIFIED Requirements

### Requirement: Capability as a self-contained unit
The system SHALL organize each agent capability — retrieval, requisition creation, and skill activation — as a self-contained unit that owns BOTH the tool handlers it registers AND the prompt fragment documenting those tools, and SHALL expose that pairing as a single definition object exposing a stable id, a display name, an action block, and its tool list, so that the unit can be bound to an agent without any of its content being re-derived.

#### Scenario: A capability's tools and their documentation are declared together
- **WHEN** a capability unit is inspected
- **THEN** the prompt fragment documenting its tools is declared by the same unit that registers those tool handlers, rather than in a separate prompt type

#### Scenario: Adding a tool keeps its documentation with it
- **WHEN** a new tool is added to a capability
- **THEN** its action-block documentation is added in the same unit, so a tool cannot be registered without the prompt text that tells the model when to call it

#### Scenario: Each capability carries a stable identity
- **WHEN** a capability unit is composed
- **THEN** it exposes a stable id and a display name that identify it independently of its tool list, so the capability remains addressable when the tools it holds are regrouped

#### Scenario: The stable id and the display name are separate values
- **WHEN** a capability is bound to an agent
- **THEN** the stable id seeds the agent's machine-readable identity and the display name remains only a human label, so a presentation rename cannot change the agent's identity

#### Scenario: A unit's content is reused rather than rewritten
- **WHEN** a capability is bound to its own agent
- **THEN** the agent's instructions and tool list are taken from the same definition object, so no action-block text or tool handler is copied or reconstructed during binding

### Requirement: Capability catalog as the single binding point
The system SHALL expose a catalog that aggregates every capability definition and is the single place where capability definitions are bound to the agent graph, and SHALL NOT have any other component assemble any agent's tool list or concatenate capability action blocks.

#### Scenario: Agent graph composed from the catalog
- **WHEN** the agent is composed
- **THEN** every agent's tool list and instructions are taken from the catalog's capability definitions, and no component outside the catalog enumerates tools or action blocks directly

#### Scenario: Catalog exposes the combined tool set
- **WHEN** a component needs the full set of tools bound across all agents
- **THEN** it reads them from the catalog rather than from an individual capability unit

#### Scenario: Adding a capability does not require editing the agent composition
- **WHEN** a new capability unit is registered
- **THEN** its tools and its action block reach an agent through the catalog, without modifying the code that composes the agents

#### Scenario: An agent is not given the catalog's combined list
- **WHEN** a capability is bound to its own agent
- **THEN** that agent is bound to the capability's own tools and action block rather than to the catalog's combined list, so extracting a capability reduces what that agent can call

### Requirement: Capability tool partition is verifiable
The system SHALL ensure that the capability units collectively expose exactly the framework's full tool set, with each tool assigned to exactly one agent, and this partition SHALL be asserted so that a duplicate, an omission, or an agent holding another capability's tool fails a test rather than silently changing what the model can call.

#### Scenario: Capabilities partition the full tool set
- **WHEN** the union of the tools bound across all agents is inspected
- **THEN** it contains exactly the framework's seven tools, and no tool name is assigned to more than one agent

#### Scenario: A duplicated tool name fails the partition check
- **WHEN** two capability units register a tool under the same wire name
- **THEN** the partition assertion fails, because a wire name is declared once and consumed by both registration and report bookkeeping

#### Scenario: An agent holding a foreign capability's tool fails the check
- **WHEN** an agent is composed with a tool owned by a different capability
- **THEN** the assignment assertion fails, because an agent must never be able to call a tool whose action block it was not given

## ADDED Requirements

### Requirement: A capability may be bound to its own agent
The system SHALL support binding a capability unit to a dedicated agent whose instructions are that unit's action block and whose tools are that unit's tools, while capabilities that are not yet bound keep their tools on the orchestrator, so capabilities can be extracted one at a time.

#### Scenario: An extracted capability answers in its own voice
- **WHEN** a turn is handed to a capability's own agent
- **THEN** that agent answers with the instructions and tools of its capability alone, and the orchestrator does not relay or restate its answer

#### Scenario: A capability not yet extracted stays reachable
- **WHEN** a capability has not been bound to its own agent
- **THEN** the orchestrator still holds its tools and action block, so the capability remains fully usable
