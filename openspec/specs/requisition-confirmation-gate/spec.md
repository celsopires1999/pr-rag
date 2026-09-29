# Requisition Confirmation Gate

## Purpose

Enforces an explicit two-step draft-then-confirm state machine in front of purchase requisition creation, so that `create_requisition` persists only from a draft the user actually saw and confirmed, independent of whether a skill activated, and records the confirmation path per turn so gate compliance is observable after the fact.

## Requirements

### Requirement: Requisition draft staging
The system SHALL expose a `create_requisition_draft` tool that records the six requisition fields (`SupplierCode`, `Item`, `Description`, `Quantity`, `Date`, `Requester`) as an unpersisted draft in the session state bag, validates the presence and format of every required field, and marks the draft as presented to the user. Staging a draft SHALL NOT write anything to the database. The tool SHALL overwrite any prior unconfirmed draft in the same session and SHALL clear the draft's confirmed state.

#### Scenario: Valid fields staged as a presented draft
- **WHEN** the model calls `create_requisition_draft` with all six fields present and well-formed
- **THEN** the system stores the draft in session state, marks it presented, persists nothing to the database, and returns the draft fields to the model for display

#### Scenario: Invalid fields rejected at staging
- **WHEN** the model calls `create_requisition_draft` with a missing required field, a non-positive `Quantity`, or a `Date` that is not ISO `yyyy-MM-dd`
- **THEN** the system stores no draft, marks nothing presented, and returns a structured error naming the invalid fields

#### Scenario: Re-drafting supersedes the previous draft
- **WHEN** the user changes a field after a draft was already staged and the model calls `create_requisition_draft` again
- **THEN** the system replaces the stored draft with the new values and clears the confirmed state, so the previous draft can no longer be persisted

### Requirement: Explicit draft confirmation
The system SHALL expose a `confirm_requisition_draft` tool that marks the session's presented draft as confirmed only when the model passes an explicit affirmative answer from the user. The tool SHALL refuse to confirm when no draft is staged, when the draft is not yet presented, or when the answer is anything other than an explicit yes. A confirmed draft SHALL be immutable: any subsequent staging of different field values SHALL clear the confirmed state.

#### Scenario: Affirmative answer confirms a presented draft
- **WHEN** the model calls `confirm_requisition_draft` with an explicit yes after a draft has been staged and presented
- **THEN** the system marks the draft confirmed and returns the confirmed field set that `create_requisition` will persist

#### Scenario: Negative answer leaves the draft unconfirmed
- **WHEN** the user declines and the model calls `confirm_requisition_draft` with a no
- **THEN** the system leaves the draft unconfirmed, persists nothing, and returns a result instructing the model to keep the draft open for revision

#### Scenario: Confirmation refused with no presented draft
- **WHEN** the model calls `confirm_requisition_draft` and the session has no presented draft
- **THEN** the system confirms nothing and returns an error directing the model to stage a draft first

#### Scenario: Editing after confirmation requires re-confirmation
- **WHEN** a draft was confirmed and the model then calls `create_requisition_draft` with different field values
- **THEN** the system stores the new values, clears the confirmed state, and requires a fresh presentation and confirmation before the requisition can be persisted

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

### Requirement: Requisition creation requires a confirmed draft
The system SHALL refuse to persist a purchase requisition unless the session holds a draft marked confirmed by `confirm_requisition_draft`. When no confirmed draft exists, `create_requisition` SHALL persist nothing and SHALL return a structured refusal naming `create_requisition_draft` and `confirm_requisition_draft` as the required prior steps. The values persisted SHALL be taken from the confirmed draft, and the tool arguments SHALL be rejected as inconsistent when they differ from any confirmed draft field.

This requirement is independent of the item-supplier combination guard: a confirmed draft that names an unregistered combination SHALL still be refused, and a registered combination SHALL still be refused without a confirmed draft.

#### Scenario: Confirmed draft persists the requisition
- **WHEN** the model calls `create_requisition` with arguments matching a confirmed draft and an existing requisition has the same item and supplier combination
- **THEN** the system persists the confirmed draft's field values in the dedicated Postgres schema, clears the session's draft state, and returns the created requisition id

#### Scenario: Refused when no confirmed draft exists
- **WHEN** the model calls `create_requisition` with six valid fields but the session has no confirmed draft
- **THEN** the system persists nothing, returns a structured refusal naming the draft and confirmation tools as the required prior steps, and does not report a requisition id

#### Scenario: Refused when arguments contradict the confirmed draft
- **WHEN** the model calls `create_requisition` and any argument differs from the corresponding field of the confirmed draft
- **THEN** the system persists nothing and returns a refusal naming the field that differs and restating the confirmed value, so the model can re-draft

#### Scenario: Draft state cleared after successful creation
- **WHEN** a requisition is persisted from a confirmed draft
- **THEN** the session no longer holds a draft, a presented flag, or a confirmed flag, so a second `create_requisition` in the same session is refused

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
