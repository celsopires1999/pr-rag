## MODIFIED Requirements

### Requirement: Capabilities are bound as agents behind a workflow graph
The system SHALL bind each extracted capability to its own agent, compose an orchestrator agent that routes between them, and present the resulting graph to callers as a single agent through `WorkflowHostingExtensions.AsAIAgent(...)`, so that the application runs one composed agent regardless of how many participants the graph holds. A capability that owns a write-path tool SHALL NOT remain bound to the orchestrator once the change is delivered, so that the orchestrator can neither call a write tool nor answer a request that requires one. The orchestrator SHALL NOT present work product it did not obtain from a tool call, because holding no write tool prevents an unsound write but does not prevent the orchestrator from describing a write that never happened.

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

#### Scenario: Holding no write tool does not license describing one
- **WHEN** a creation request reaches the orchestrator, which holds no tool that stages a draft
- **THEN** the orchestrator hands the turn to the agent that owns the write capability rather than presenting a draft summary or asking the user to confirm one, because a draft it cannot stage is one it must not describe as staged

#### Scenario: An answer obtained from a tool is not restated as unearned work product
- **WHEN** an agent's turn ends with text it produced rather than with a result a tool returned
- **THEN** that text is not presented to the user as the output of a staged, confirmed, or persisted artifact, because the absence of a tool result is the only evidence that the artifact does not exist
