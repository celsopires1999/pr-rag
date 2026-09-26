# Specialist Capabilities

## Purpose
Group the agent's tools into per-capability units that each own both their tool handlers and the prompt fragment documenting them, so a tool can never be registered without the text that tells the model when to call it, and a new capability can be added without editing agent composition.

## Requirements

### Requirement: Capability as a self-contained unit
The system SHALL organize each agent capability — retrieval, requisition creation, and skill activation — as a self-contained unit that owns BOTH the tool handlers it registers AND the prompt fragment documenting those tools, and SHALL expose that pairing as a single definition object exposing an id, a display name, an action block, and its tool list.

#### Scenario: A capability's tools and their documentation are declared together
- **WHEN** a capability unit is inspected
- **THEN** the prompt fragment documenting its tools is declared by the same unit that registers those tool handlers, rather than in a separate prompt type

#### Scenario: Adding a tool keeps its documentation with it
- **WHEN** a new tool is added to a capability
- **THEN** its action-block documentation is added in the same unit, so a tool cannot be registered without the prompt text that tells the model when to call it

#### Scenario: Each capability carries a stable identity
- **WHEN** a capability unit is composed
- **THEN** it exposes an id and a display name that identify it independently of its tool list, so the capability remains addressable when the tools it holds are regrouped

### Requirement: Capability catalog as the single binding point
The system SHALL expose a catalog that aggregates every capability definition and is the single place where capability definitions are bound to the agent, and SHALL NOT have any other component assemble the agent's tool list or concatenate capability action blocks.

#### Scenario: Agent composed from the catalog
- **WHEN** the agent is composed
- **THEN** its tool list is the concatenation of every capability's tools, taken from the catalog, and no component outside the catalog enumerates tools or action blocks directly

#### Scenario: Catalog exposes the combined tool list
- **WHEN** a component needs the full set of tools bound to the agent
- **THEN** it reads them from the catalog rather than from an individual capability unit

#### Scenario: Adding a capability does not require editing the agent composition
- **WHEN** a new capability unit is registered
- **THEN** its tools and its action block are included in the agent through the catalog, without modifying the code that composes the agent

### Requirement: Capability tool partition is verifiable
The system SHALL ensure that the capability units collectively expose exactly the framework's full tool set, with each tool exposed by exactly one capability, and this partition SHALL be asserted so that a duplicate or an omission fails a test rather than silently changing what the model can call.

#### Scenario: Capabilities partition the full tool set
- **WHEN** the catalog's combined tool list is inspected
- **THEN** it contains exactly the framework's seven tools, and no tool name appears in more than one capability

#### Scenario: A duplicated tool name fails the partition check
- **WHEN** two capability units register a tool under the same wire name
- **THEN** the partition assertion fails, because a wire name is declared once and consumed by both registration and report bookkeeping

### Requirement: Shared tool registration mechanics
The system SHALL provide a single shared helper through which every capability registers its handlers, records its per-turn tool calls, and logs its invocations, and SHALL NOT duplicate that registration, bookkeeping, or logging logic per capability.

#### Scenario: Registration mechanics are not duplicated per capability
- **WHEN** each capability unit is inspected
- **THEN** it registers its handlers, records tool calls, and logs through the shared helper rather than through its own copy of that logic

#### Scenario: Registered handlers keep their method metadata
- **WHEN** a capability registers a tool through the shared helper
- **THEN** the annotated handler method itself is registered, so the parameter descriptions on that method reach the generated JSON schema

#### Scenario: Per-turn bookkeeping is shared across capabilities
- **WHEN** any capability's tool is invoked during a turn
- **THEN** the invocation is recorded in the same per-turn bookkeeping that feeds the observability report, so the report is identical regardless of which capability owned the tool
