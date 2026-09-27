## ADDED Requirements

### Requirement: A routed turn is attributable to its entry agent and its handoffs
The system SHALL record, per turn, the agent that the turn was composed at and every handoff taken during it, so that a turn handled outside the orchestrator and a turn that delegated to a specialist are both distinguishable from a turn the orchestrator answered directly. The handoff entries SHALL name the source agent and the targeted capability, because a delegation that cannot be attributed to a capability cannot be diagnosed when the delegated answer is wrong.

#### Scenario: An orchestrator-answered turn records no handoff
- **WHEN** a turn is composed at the orchestrator and answered without delegating
- **THEN** the report records the orchestrator as the entry agent and an empty handoff list, so a direct answer is distinguishable from a delegation

#### Scenario: A delegated turn records the delegation
- **WHEN** an agent hands the turn to a participant
- **THEN** the report records a handoff naming the source agent and the targeted capability, so the delegation is visible in the report rather than only in logs

#### Scenario: A turn entered at a specialist names that specialist
- **WHEN** a turn is composed at a participant rather than at the orchestrator
- **THEN** the report names that participant as the entry agent, so the reason the orchestrator is absent from the turn is recoverable from the report

#### Scenario: An entry agent and a handoff are consistent
- **WHEN** a report records a handoff whose source differs from the entry agent
- **THEN** the handoff chain is preserved in order, so a multi-step delegation is reconstructable rather than only its endpoints

#### Scenario: A turn with no delegation remains a valid report
- **WHEN** a turn completes with no handoffs
- **THEN** the report is still written and carries an empty handoff list, so adding these fields does not make the single-agent case exceptional

#### Scenario: One delegation is one entry however many times it is observed
- **WHEN** the same delegation is observed more than once, as a streamed call is: the function name in one update and its argument deltas in others
- **THEN** the report records it once, because a fraction of a handoff counted as several delegations would misreport the routing it exists to describe

#### Scenario: A turn does not inherit an earlier turn's delegations
- **WHEN** a turn begins in a context that already recorded handoffs
- **THEN** that list is reset, so the report describes only the delegations the turn that wrote it took, and an unrouted turn cannot read as routed
