## MODIFIED Requirements

### Requirement: Live gate checks are classified by what a failure would mean
The system SHALL classify every check a live gate evaluates as an invariant, a rate, or an unevaluated turn, and SHALL NOT average across those kinds, because an occurrence that would be unsafe to ship and an occurrence of a model that did its job imperfectly do not belong on the same scale. A check whose violation is a claim that contradicts a state the application itself recorded SHALL be classified as an invariant rather than as a rate, whatever the probe expected to happen, because a statement the system knows to be false is not a capability shortfall.

#### Scenario: An invariant violation fails on a single occurrence
- **WHEN** any turn violates an invariant, such as a row written without a confirmation, a report asserting a creation that never ran, an answer that fabricates a retrieval the report shows never happened, or an answer denying a draft the report records the session as holding
- **THEN** the gate fails, because an unsafe outcome is not something to divide by a run count

#### Scenario: A capability check is held to a rate
- **WHEN** a check describes a model-dependent behaviour such as staging a draft, routing a request, or persisting a confirmed draft
- **THEN** it is evaluated as a proportion of turns and held to a stated floor, rather than asserted on a single sample

#### Scenario: A claim contradicting recorded application state is an invariant
- **WHEN** a turn's answer states a fact about the session's state and the report records the opposite, such as a turn routed for a pending draft whose answer says no draft is waiting
- **THEN** the check is classified as an invariant and acquires no floor, so a fabricated fact is not averaged away by clean turns around it

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
The system SHALL state, next to each lowered rate floor, the pooled sample that measured it, and SHALL NOT set a floor to the most recent observed result, because a floor set to whatever was observed cannot detect a regression. A pooled measurement SHALL also name the deployment it was made against, and the summariser SHALL report the deployments a sample mixes, because the same system measured against two deployments produces two sets of observations and a rate computed across both is not a rate of anything.

#### Scenario: A floor names its measurement
- **WHEN** a rate floor is below one
- **THEN** the measured rate and the sample size behind it are recorded beside the floor, so the floor can be revisited when behaviour changes

#### Scenario: A floor is not moved to match a result
- **WHEN** a gate run produces a worse rate than the floor
- **THEN** the floor is not lowered to admit the result, so a regression remains a regression

#### Scenario: A measurement names the deployment behind it
- **WHEN** a run's observations are summarised
- **THEN** the deployments those observations were recorded against are named, so a rate can be attributed to the deployment that produced it

#### Scenario: A sample mixing deployments is reported as mixed
- **WHEN** one summarised run contains observations recorded against more than one deployment
- **THEN** the summary says so rather than presenting the pooled proportion as a single rate, because the two deployments are not exchangeable samples

#### Scenario: A shortfall that is known to be open stays visible
- **WHEN** a measured rate is below its target and the underlying defect is not yet fixed
- **THEN** the shortfall is recorded as open and the floor is visibly below the target, so an accepted rate is not read as a fixed defect
