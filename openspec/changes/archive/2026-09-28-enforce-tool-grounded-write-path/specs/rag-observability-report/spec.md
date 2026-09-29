## ADDED Requirements

### Requirement: The report records the state the entry point was chosen from
The system SHALL record, per turn, whether the session held an unwritten requisition draft at the moment the turn's entry point was resolved, taken from the same state read that chose that entry point and recorded beside that decision, so that the two cannot disagree. The field SHALL be nullable, and a turn in which the entry point was never resolved SHALL record no value rather than recording that no draft existed, because a state that was never read is not an empty state. The field SHALL be read next to the recorded entry agent, since it is the reason that entry agent was chosen.

This is the state-side counterpart to the recorded retrieval and write attempts: those say what a turn did, and this says what the application held, which is what makes an agent's account of that state checkable. Without it, a turn that denies a staged draft records `RequisitionDraftStaged=false` and reads as a turn in which nothing was ever staged.

#### Scenario: A turn routed for a pending draft records that it was pending
- **WHEN** a turn begins with an unwritten draft in the session and the entry point resolves to the write capability
- **THEN** the report records that a draft was pending at entry and names that capability as the entry agent, so the routing decision and the state that caused it are read together

#### Scenario: A turn with no pending draft records no draft pending
- **WHEN** a turn begins with no unwritten draft in the session and the entry point resolves to the orchestrator
- **THEN** the report records that no draft was pending at entry, so the common turn carries the fact explicitly rather than omitting it

#### Scenario: A turn that never composed records no value
- **WHEN** a turn ends before the entry point is resolved
- **THEN** the report records no pending-draft-at-entry value, so an unevaluated turn is distinguishable from one that read an empty state

#### Scenario: An agent's account of the session's draft state is falsifiable
- **WHEN** a turn records a draft pending at entry, called no write tool, and its answer states that no draft is waiting
- **THEN** the report shows the contradiction between the state the application held and the answer the caller received, so the turn is diagnosable from the report alone
