## ADDED Requirements

### Requirement: Retrieval and write attempts are separately observable
The system SHALL record per turn, independently of the tool call list, whether a
read tool was invoked and whether a write tool was invoked, so that a turn which
searched and found nothing is distinguishable from a turn that never searched,
and a turn that staged a draft is distinguishable from a turn that recorded a
confirmation and wrote a row. The report SHALL record a draft staged, a
confirmation recorded, and a row written as three separate facts, because
`RetrievedCount == 0` cannot distinguish an empty result from an absent
retrieval, and a tool call list alone cannot distinguish a refused write from an
attempted one.

#### Scenario: A search that found nothing is distinguishable from never searching
- **WHEN** a turn invoked a read tool and every call returned no rows
- **THEN** the report records a retrieval attempt, an empty retrieval, and the fallback answer

#### Scenario: A turn with no read tool records no retrieval attempt
- **WHEN** a turn invoked only write tools
- **THEN** the report records no retrieval attempt, so the turn is not counted as a retrieval

#### Scenario: A refused creation is distinguishable from an absent one
- **WHEN** `create_requisition` refuses an invalid item/supplier combination
- **THEN** the report records a write attempt that produced no row, which is distinct from a turn that never called the tool

#### Scenario: A turn claiming a creation is falsifiable from the report
- **WHEN** a turn's answer states that a requisition was created and the report is inspected
- **THEN** the report's write facts show no write attempt, so the claim can be detected without querying the table

#### Scenario: The read tool set is named rather than inferred
- **WHEN** the set of tools that count as a retrieval is defined
- **THEN** the read tools are listed explicitly, so a future tool is not silently reclassified as either a read or a write by inference
