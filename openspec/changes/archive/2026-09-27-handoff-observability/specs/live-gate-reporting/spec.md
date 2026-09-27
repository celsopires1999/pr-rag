## ADDED Requirements

### Requirement: Live gate checks are classified by what a failure would mean
The system SHALL classify every check a live gate evaluates as an invariant, a rate, or an unevaluated turn, and SHALL NOT average across those kinds, because an occurrence that would be unsafe to ship and an occurrence of a model that did its job imperfectly do not belong on the same scale.

#### Scenario: An invariant violation fails on a single occurrence
- **WHEN** any turn violates an invariant, such as a row written without a confirmation, a report asserting a creation that never ran, or an answer that fabricates a retrieval the report shows never happened
- **THEN** the gate fails, because an unsafe outcome is not something to divide by a run count

#### Scenario: A capability check is held to a rate
- **WHEN** a check describes a model-dependent behaviour such as staging a draft, routing a request, or persisting a confirmed draft
- **THEN** it is evaluated as a proportion of turns and held to a stated floor, rather than asserted on a single sample

#### Scenario: An unevaluated turn is counted separately
- **WHEN** a turn could not be evaluated at all, such as a non-2xx response, an unparseable body, or a missing report
- **THEN** it is counted and reported as unevaluated and is not treated as either a pass or a violation of the gate's subject

#### Scenario: An unevaluated turn is never reported as a pass
- **WHEN** a gate run contains one or more unevaluated turns and no violation
- **THEN** the gate does not report a pass, because a system that could not answer is not a system that behaved correctly

#### Scenario: A check not explicitly classified keeps the strict default
- **WHEN** a check has no stated classification
- **THEN** it is held to a required rate of one, so a gate is no weaker than a single-run gate unless a floor was deliberately lowered

### Requirement: A rate floor cites the measurement that justifies it
The system SHALL state, next to each lowered rate floor, the pooled sample that measured it, and SHALL NOT set a floor to the most recent observed result, because a floor set to whatever was observed cannot detect a regression.

#### Scenario: A floor names its measurement
- **WHEN** a rate floor is below one
- **THEN** the measured rate and the sample size behind it are recorded beside the floor, so the floor can be revisited when behaviour changes

#### Scenario: A floor is not moved to match a result
- **WHEN** a gate run produces a worse rate than the floor
- **THEN** the floor is not lowered to admit the result, so a regression remains a regression

#### Scenario: A shortfall that is known to be open stays visible
- **WHEN** a measured rate is below its target and the underlying defect is not yet fixed
- **THEN** the shortfall is recorded as open and the floor is visibly below the target, so an accepted rate is not read as a fixed defect

### Requirement: A clean gate run is distinguishable from a run that examined nothing
The system SHALL report, for a run in which no check failed, how many turns each probe raised nothing on, and SHALL fail a probe that never reported at all, because a check that passes leaves no row and an absent row is otherwise indistinguishable from a check that never ran.

#### Scenario: A clean run still shows what was checked
- **WHEN** a gate run completes with no violation
- **THEN** it reports the per-probe turn counts that raised nothing, so a clean result is readable as checked-and-clean

#### Scenario: An empty table is stated as such
- **WHEN** no check failed and therefore no failure rows exist
- **THEN** the summary says so explicitly rather than printing an empty table

#### Scenario: A probe that never reported fails the run
- **WHEN** a probe the gate expected to exercise produced no turn
- **THEN** the gate fails, because checks that never ran cannot have passed

#### Scenario: No observations at all fails the run
- **WHEN** a gate run recorded no turns
- **THEN** the gate fails, because nothing was checked

### Requirement: The gate verdict is derived from the recorded observations
The system SHALL compute a gate's verdict from the recorded observations rather than from counters accumulated while running, so that the per-turn output and the aggregate verdict cannot disagree.

#### Scenario: The human-readable line and the record come from one evaluation
- **WHEN** a turn is evaluated
- **THEN** the line shown to the operator and the observation recorded for aggregation are produced by the same evaluation of that turn

#### Scenario: A counter cannot override the aggregate
- **WHEN** the shell-level pass and fail counts would disagree with the recorded observations
- **THEN** the recorded observations determine the verdict, because the counts are a convenience rather than the source of truth
