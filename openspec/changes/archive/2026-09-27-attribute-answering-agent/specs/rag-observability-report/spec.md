## ADDED Requirements

### Requirement: A turn's answer is attributable to the agent that authored it
The system SHALL record, per turn, the capability slug of the agent that authored the turn's answer, so that a turn the entry agent answered without delegating is distinguishable from a turn it delegated to an agent which then answered badly. The recorded author SHALL be the last agent to emit non-empty text, because the run's answer is that text, so the record cannot name an agent that did not write what the caller received. A response that emits only a function call SHALL NOT claim authorship, because the common mid-loop turn makes no text and attributing an answer to whichever agent called a tool last would misreport it. A turn in which no agent emitted text SHALL record no author rather than falling back to the entry agent, because a defaulted value is indistinguishable from a real answer and would hide the turn that produced nothing.

#### Scenario: A delegated turn names the target as the author
- **WHEN** an agent hands the turn to a participant which then produces the answer
- **THEN** the report names that participant as the answer agent, so the report says which agent wrote the text rather than only which agents were involved

#### Scenario: An answer authored at the entry point names the entry agent
- **WHEN** a turn is answered without delegating
- **THEN** the report names the entry agent as both entry and answer agent, so an answer given in place and a delegated answer are distinguishable

#### Scenario: An entry agent and a handoff are no longer the whole story
- **WHEN** a report records a handoff and the answer agent is the entry agent
- **THEN** the report is still a valid report and the chain is intact, so a turn that delegated and was then answered by the entry agent is readable rather than contradictory

#### Scenario: A tool call alone does not make an agent the author
- **WHEN** an agent's response contains a function call and no text
- **THEN** that agent is not recorded as the answer agent, so a mid-loop tool call cannot be mistaken for authorship of the turn's answer

#### Scenario: A streamed answer is attributed once it has text
- **WHEN** a turn's answer arrives as streamed updates from more than one agent
- **THEN** the recorded author is the last agent that emitted text, so partial updates from an earlier agent do not claim an answer a later agent produced

#### Scenario: A turn that produced no answer records no author
- **WHEN** a run completes with no text from any agent, as a failed provider call does
- **THEN** the report is still written and records no answer agent, so a turn that reached no author is distinguishable from one whose answer was merely empty

#### Scenario: A turn does not inherit an earlier turn's author
- **WHEN** a turn begins in a context that already recorded an answer agent
- **THEN** that field is reset, so the report describes only the turn that wrote it and an unrouted turn cannot read as a delegation

#### Scenario: The author is recorded for a turn that then fails
- **WHEN** a turn yields no answer and the request fails
- **THEN** the report is written before the failure surfaces and carries the author recorded so far, so a failed turn remains diagnosable rather than leaving only a status code
