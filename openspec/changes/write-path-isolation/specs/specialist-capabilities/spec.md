## MODIFIED Requirements

### Requirement: Capability tool partition is verifiable
The system SHALL ensure that the capability units collectively expose exactly the framework's full tool set, with each tool exposed by exactly one capability, and this partition SHALL be asserted so that a duplicate or an omission fails a test rather than silently changing what the model can call. The system SHALL further ensure that every capability is fully owned by exactly one agent: a unit bound to its own agent exposes its tools there, and no agent, including the orchestrator, exposes a tool owned by a different unit.

#### Scenario: Capabilities partition the full tool set
- **WHEN** the catalog's combined tool list is inspected
- **THEN** it contains exactly the framework's seven tools, and no tool name appears in more than one capability

#### Scenario: A duplicated tool name fails the partition check
- **WHEN** two capability units register a tool under the same wire name
- **THEN** the partition assertion fails, because a wire name is declared once and consumed by both registration and report bookkeeping

#### Scenario: No agent holds another capability's tool
- **WHEN** the graph is composed and each participant's tool set is inspected
- **THEN** no participant can call a tool owned by a different capability, and in particular the orchestrator holds no tool that writes

#### Scenario: An unbound capability still reachable by its owner
- **WHEN** a capability has not been bound to its own agent
- **THEN** the orchestrator still holds that capability's tools and action block, and the assertion continues to permit exactly one such leftover rather than allowing an arbitrary one

## ADDED Requirements

### Requirement: A confirmation turn re-enters the write capability deterministically
The system SHALL route a turn on which a draft requisition is awaiting the user's confirmation to the capability that owns that draft, and SHALL base that routing on the draft's own recorded state rather than on the model inferring from the conversation that a confirmation is due.

#### Scenario: A staged draft is confirmed by the same capability that staged it
- **WHEN** a previous turn staged a draft and the user replies confirming it
- **THEN** the turn is handled by the creation capability, which records the confirmation and writes the requisition

#### Scenario: The orchestrator is not asked to re-derive that a draft is pending
- **WHEN** a turn arrives while a draft is awaiting confirmation
- **THEN** no read of the orchestrator is required to establish that fact, because the entry point is selected from the draft's state

#### Scenario: An unrelated request during a pending confirmation does not write
- **WHEN** a draft is awaiting confirmation and the user asks an unrelated question instead of confirming
- **THEN** no requisition row is written and the draft remains staged for a later confirmation

#### Scenario: A claimed confirmation with no staged draft writes nothing
- **WHEN** the user states a confirmation for a draft that was never staged in this session
- **THEN** no requisition row is written and the answer does not assert that one was created
