## ADDED Requirements

### Requirement: Capabilities are bound as agents behind a workflow graph
The system SHALL bind each extracted capability to its own agent, compose an orchestrator agent that routes between them, and present the resulting graph to callers as a single agent through `WorkflowHostingExtensions.AsAIAgent(...)`, so that the application runs one composed agent regardless of how many participants the graph holds.

#### Scenario: The graph is presented to callers as one agent
- **WHEN** the application composes the agent
- **THEN** the orchestrator and its participants are joined into a workflow and exposed through a single `AIAgent`, so the caller-facing seam does not change shape when participants are added

#### Scenario: A capability that has not been extracted yet remains on the orchestrator
- **WHEN** a capability is not yet bound to its own agent
- **THEN** the orchestrator still holds that capability's tools, so no capability becomes unreachable during a staged migration

#### Scenario: The transitional topology is not presented as least privilege
- **WHEN** the orchestrator still holds a write-path capability's tools
- **THEN** the change is documented as partial, and write-path isolation is not claimed as delivered

### Requirement: Handoff is the delegation mechanism
The system SHALL transfer a conversation between agents by handoff rather than by invoking a specialist as a tool from the orchestrator, so that a specialist which needs to ask the user a question does so directly instead of having its text relayed and paraphrased.

#### Scenario: A specialist asks the user directly
- **WHEN** a specialist needs a further value from the user
- **THEN** the specialist addresses the user itself, and the orchestrator does not paraphrase the specialist's question back to the user

#### Scenario: The conversation returns to the orchestrator
- **WHEN** a participant has finished the work it was handed
- **THEN** the conversation returns to the orchestrator so it can produce the turn's answer, rather than the participant's intermediate text becoming the final answer

### Requirement: Agents have stable identities distinct from display names
The system SHALL give every agent a stable machine-readable slug used for telemetry correlation and handoff resolution, held separately from its human-facing display name, so that renaming an agent for presentation does not break telemetry or routing.

#### Scenario: A display rename leaves telemetry identity intact
- **WHEN** an agent's display name is changed
- **THEN** its slug, and therefore its telemetry correlation and routing identity, is unchanged

#### Scenario: The slug is seeded from the capability definition
- **WHEN** an agent is created for a capability unit
- **THEN** its slug derives from that unit's existing stable id, so identity is not invented anew during the migration

### Requirement: A specialist holds only its own capability's tools
The system SHALL bind each agent's tool list to exactly the tools of the capabilities it owns, so that no agent is told about a tool it cannot call and no agent can call a tool whose documentation it has not been given.

#### Scenario: A specialist sees only its own action block
- **WHEN** a specialist agent is composed
- **THEN** its instructions are the action block of its own capability alone, and it is given only that capability's tools

#### Scenario: Tool assignment is a per-agent partition
- **WHEN** the tools bound to each agent are inspected
- **THEN** each tool is held by exactly one agent, and the union across agents equals the full framework tool set with none added, removed, or duplicated

#### Scenario: An agent holding another capability's tools fails the check
- **WHEN** an agent is composed with a tool owned by a different capability
- **THEN** the assignment assertion fails, because an agent must not be able to call a tool it was never instructed about

### Requirement: Specialist turns are scoped to the specialist's own session
The system SHALL scope a specialist's turn context to that specialist's own session rather than sharing the orchestrator's, so that state a specialist accumulates is not carried in the orchestrator's context.

#### Scenario: A specialist's context is not the orchestrator's
- **WHEN** a specialist is handed a turn
- **THEN** the state available to it is its own, and it does not read or write the orchestrator's turn context

#### Scenario: Session plumbing exists before a specialist depends on it
- **WHEN** a capability is not yet bound to its own agent
- **THEN** the per-specialist session mechanism is nevertheless available, so a later extraction does not require re-plumbing sessions

### Requirement: Delegation is observable without changing the observability report
The system SHALL record routing and handoff activity through application logging, and SHALL NOT add fields to the RAG observability report, so that report consumers are unaffected by the introduction of a multi-agent graph.

#### Scenario: Routing is visible in logs
- **WHEN** the orchestrator hands a turn to a participant
- **THEN** the handoff is recorded through the logging infrastructure, naming the orchestrator and the participant

#### Scenario: The report schema is unchanged
- **WHEN** a report is written for a turn that was routed between agents
- **THEN** every report field carries the same value and naming it did before the graph was introduced

### Requirement: The multi-agent graph is verified live, not only by the suite
The system SHALL gate the introduction of routing on a live walkthrough that exercises a retrieval question, a handoff to a specialist, and a return to the orchestrator, and a live result materially worse than the pre-change baseline SHALL block further capability extraction rather than ship.

#### Scenario: A live routing regression blocks the next extraction
- **WHEN** the live walkthrough shows a worse result than the pre-change baseline
- **THEN** the follow-up extraction is not started until the regression is understood, and the baseline is re-run to attribute the failure to the topology or to the model

#### Scenario: A live failure is attributed before it is dismissed
- **WHEN** a live walkthrough fails
- **THEN** the same walkthrough is run against the pre-change code before concluding the graph caused it, because this model is already known to fail multi-turn flows that also failed before the change
