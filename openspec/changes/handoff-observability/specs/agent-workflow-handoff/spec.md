## MODIFIED Requirements

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

## ADDED Requirements

### Requirement: A handoff is observed where the emitting agent is still known
The system SHALL observe handoff calls at a point that can identify which agent emitted them and that runs before the call is dispatched, and SHALL NOT rely on the application tool layer for this, because a handoff is a workflow edge rather than an application tool and dispatches outside it.

#### Scenario: The emitting agent is identified at observation time
- **WHEN** a handoff call is observed
- **THEN** the observer knows which agent emitted it, because the observation is bound per agent rather than resolved afterwards

#### Scenario: A refusal inside the dispatch layer is not mistaken for an absent handoff
- **WHEN** a handoff is emitted but the target refuses or no-ops
- **THEN** the handoff is still recorded, so an observed refusal is distinguishable from no delegation having been attempted
