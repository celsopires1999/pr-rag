# agent-workflow-handoff Specification

## Purpose
TBD - created by archiving change agent-workflow-handoff. Update Purpose after archive.
## Requirements
### Requirement: Capabilities are bound as agents behind a workflow graph
The system SHALL bind each extracted capability to its own agent, compose an orchestrator agent that routes between them, and present the resulting graph to callers as a single agent through `WorkflowHostingExtensions.AsAIAgent(...)`, so that the application runs one composed agent regardless of how many participants the graph holds. A capability that owns a write-path tool SHALL NOT remain bound to the orchestrator once the change is delivered, so that the orchestrator can neither call a write tool nor answer a request that requires one.

#### Scenario: The graph is presented to callers as one agent
- **WHEN** the application composes the agent
- **THEN** the orchestrator and its participants are joined into a workflow and exposed through a single `AIAgent`, so the caller-facing seam does not change shape when participants are added

#### Scenario: A capability that has not been extracted yet remains on the orchestrator
- **WHEN** a capability that owns no write-path tool has not been bound to its own agent
- **THEN** the orchestrator still holds that capability's tools, so no capability becomes unreachable during a staged migration

#### Scenario: Write-path isolation is not claimed while the orchestrator holds a write tool
- **WHEN** the orchestrator still holds a write-path capability's tools
- **THEN** the change is documented as partial, and write-path isolation is not claimed as delivered

#### Scenario: The orchestrator cannot act on a write-path request
- **WHEN** the orchestrator is composed after write-path isolation is delivered
- **THEN** the orchestrator holds no write tool, so a request that requires one can only be satisfied by a handoff to the agent that owns it

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

### Requirement: Delegation is observable in the observability report
The system SHALL record the agent that entered a turn and every handoff taken during it in the RAG observability report, and SHALL resolve each handoff to a capability rather than leaving it as the framework's positional participant identifier, so that a turn which was routed is attributable to the agent that entered it and to the capability each handoff targeted.

#### Scenario: Routing is visible in logs
- **WHEN** the orchestrator hands a turn to a participant
- **THEN** the handoff is recorded through the logging infrastructure, naming the orchestrator and the participant

#### Scenario: A routed turn names the agent that entered it
- **WHEN** a report is written for a turn that was composed at a participant rather than at the orchestrator
- **THEN** the report names that participant as the entry agent, so a turn handled outside the orchestrator is distinguishable from one it entered

#### Scenario: A handoff is recorded with the capability it targeted
- **WHEN** an agent emits a handoff call during a turn
- **THEN** the report records the handoff with the source agent and the targeted capability, resolved from the positional identifier the framework emits

#### Scenario: A handoff is recorded even when the target never runs the turn
- **WHEN** a handoff is emitted and the turn does not complete
- **THEN** the handoff is still recorded, because the delegation attempt is itself the diagnostic fact

#### Scenario: An out-of-range handoff target is an error rather than a wrong attribution
- **WHEN** a handoff name resolves to a participant position outside the composed graph
- **THEN** the resolution raises an error, because a handoff attributed to the wrong capability is worse than a turn that failed

### Requirement: A handoff is observed where the emitting agent is still known
The system SHALL observe handoff calls at a point that can identify which agent emitted them and that runs before the call is dispatched, and SHALL NOT rely on the application tool layer for this, because a handoff is a workflow edge rather than an application tool and dispatches outside it.

#### Scenario: The emitting agent is identified at observation time
- **WHEN** a handoff call is observed
- **THEN** the observer knows which agent emitted it, because the observation is bound per agent rather than resolved afterwards

#### Scenario: A refusal inside the dispatch layer is not mistaken for an absent handoff
- **WHEN** a handoff is emitted but the target refuses or no-ops
- **THEN** the handoff is still recorded, so an observed refusal is distinguishable from no delegation having been attempted

### Requirement: The multi-agent graph is verified live, not only by the suite
The system SHALL gate the introduction of routing on a live walkthrough that exercises a retrieval question, a handoff to a specialist, and a return to the orchestrator, and a live result materially worse than the pre-change baseline SHALL block further capability extraction rather than ship.

#### Scenario: A live routing regression blocks the next extraction
- **WHEN** the live walkthrough shows a worse result than the pre-change baseline
- **THEN** the follow-up extraction is not started until the regression is understood, and the baseline is re-run to attribute the failure to the topology or to the model

#### Scenario: A live failure is attributed before it is dismissed
- **WHEN** a live walkthrough fails
- **THEN** the same walkthrough is run against the pre-change code before concluding the graph caused it, because this model is already known to fail multi-turn flows that also failed before the change

