## ADDED Requirements

### Requirement: A claim about the session's draft state is grounded in a tool result
The system SHALL NOT have an agent assert whether a requisition draft is staged, presented, confirmed, or awaiting confirmation on the basis of the conversation's wording. A turn whose shape matches a confirmation SHALL be answered by calling `confirm_requisition_draft` and reporting what that tool returns, and the tool's result SHALL be the only authority on whether a draft is waiting — including the case in which it reports that no draft is staged. The instructions for the capability that owns the write tools SHALL state that the tool call is the only step that advances the write path, because a persisted requisition requires a confirmation that only the tool records, so no correct prose answer moves the state forward.

This is a prohibition on a *conditioned* assertion. A rule whose antecedent the model can recognise in the conversation and whose consequent is a fact only the application holds produces the denial, because nothing in such a rule tells the model to check the premise; the prohibition against asserting an unstaged draft is unaffected and remains.

#### Scenario: A confirmation-shaped turn is answered by calling the confirmation tool
- **WHEN** a turn arrives whose wording answers a draft, such as "yes, I confirm it" or "go ahead and create the requisition"
- **THEN** the agent calls `confirm_requisition_draft` and reports the tool's result, and the answer does not state whether a draft is waiting unless a tool result said so

#### Scenario: The tool's own refusal is the answer when no draft is staged
- **WHEN** `confirm_requisition_draft` reports that no draft is staged in the session
- **THEN** the agent's answer is that tool result, so the statement that no draft exists originates in code rather than in the model's reading of the conversation

#### Scenario: A staged draft is not denied on a turn the application routed for it
- **WHEN** a turn is routed to the write capability because the session held an unwritten draft, and the user answers a confirmation
- **THEN** the turn does not state that no draft is awaiting confirmation, because the application already holds one and the model has no current source for such a claim

#### Scenario: The prohibition on unstaged drafts survives the change
- **WHEN** an agent is about to describe a draft
- **THEN** it does not describe one that no tool returned, and that prohibition is still stated in the owning capability's instructions

#### Scenario: The turn is not made into a forced tool chain
- **WHEN** a confirmation-shaped turn is answered
- **THEN** the model still authors the answer from the tool's result, so the capability's prompt states which call to make rather than replacing the model on the write path

## MODIFIED Requirements

### Requirement: Confirmation gate observability
The system SHALL record, for every chat turn, whether a requisition draft was staged, presented, and confirmed, and whether a `create_requisition` call was persisted or refused. The recorded path SHALL distinguish a confirmed creation from a refused one so that gate compliance is observable after the fact. The system SHALL ALSO record whether the session held an unwritten draft at the moment the turn's entry point was resolved, taken from the same state read that chose that entry point, because the staged/confirmed/persisted facts are per-turn outcomes and an outcome of `false` on a turn the application routed for a pending draft is indistinguishable from a turn in which nothing was ever staged.

#### Scenario: Confirmed creation is recorded
- **WHEN** a turn drafts, confirms, and persists a requisition
- **THEN** the turn's report records a confirmed draft, a persisted `create_requisition` call, and the created requisition id

#### Scenario: Refused creation is recorded
- **WHEN** a turn calls `create_requisition` and the tool refuses because no confirmed draft exists
- **THEN** the turn's report records a refused `create_requisition` call and the absence of a confirmed draft

#### Scenario: Turn with no requisition activity records nothing
- **WHEN** a turn performs no draft, confirmation, or creation activity
- **THEN** the turn's report records no draft activity and no confirmation path, leaving the existing report fields unchanged

#### Scenario: The state the application held is distinguishable from what the turn did
- **WHEN** a turn records no draft staged while the session held an unwritten draft when the turn began
- **THEN** the report also records that the draft was pending at entry, so a turn that denied a held draft is distinguishable from a turn in which nothing was staged

#### Scenario: A turn that never resolved an entry point records no state
- **WHEN** a turn ends before the entry point is resolved, as a failed provider call does
- **THEN** the report records no pending-draft-at-entry value rather than recording that no draft existed, so a state that was never read is not reported as an empty one
