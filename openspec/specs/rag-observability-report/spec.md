# RAG Observability Report

## Purpose

Per-request observability tracing for the RAG chat pipeline, capturing question-to-answer traces and persisting them as machine-readable report files for debugging and analysis.

## Requirements

### Requirement: Per-request RAG observability report
The system SHALL capture a per-request observability trace for each answered chat question, covering the full question-to-answer pipeline, and SHALL persist it as a machine-readable file in a local, non-committed output directory. The report SHALL include the timestamp, the original question, the effective `top_k` and `min_similarity` (noting whether each came from the request or the configured defaults), the rewritten query when vector search is performed, the retrieved requisitions with their similarity scores, the tools invoked during the turn together with their arguments, and the final answer sent to the user.

#### Scenario: Report generated for a grounded answer
- **WHEN** a client sends `POST /api/chat` with a question and the system produces an answer
- **THEN** the system writes a report file containing the timestamp, original question, effective `top_k` and `min_similarity`, retrieved requisitions with similarity scores, and the final answer

#### Scenario: Report records effective parameters
- **WHEN** the client omits `top_k` or `min_similarity`
- **THEN** the report records the configured default value for each omitted parameter and indicates it came from defaults

#### Scenario: Report captures rewritten query
- **WHEN** vector search is performed after the question is rewritten
- **THEN** the report records the rewritten query alongside the original question

#### Scenario: Report captures no-context fallback
- **WHEN** no relevant context is found and the system returns the fallback answer
- **THEN** the report records the empty retrieval and the fallback answer sent to the user

#### Scenario: Report records tools invoked during the turn
- **WHEN** the agent invokes one or more tools (e.g. `search_semantic`, `search_by_codes`, `activate_skill`, `create_requisition`) to produce the answer
- **THEN** the report records each tool invocation with its name and arguments in the order they were called

#### Scenario: Report shows no tools when none are invoked
- **WHEN** the answer is produced without invoking any tool
- **THEN** the report records an empty tool call list

### Requirement: Retrieval and write attempts are separately observable
The system SHALL record per turn, independently of the tool call list, whether a read tool was invoked and whether a write tool was invoked, so that a turn which searched and found nothing is distinguishable from a turn that never searched, and a turn that staged a draft is distinguishable from a turn that recorded a confirmation and wrote a row. The report SHALL record a draft staged, a confirmation recorded, and a row written as three separate facts, because `RetrievedCount == 0` cannot distinguish an empty result from an absent retrieval, and a tool call list alone cannot distinguish a refused write from an attempted one.

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

### Requirement: Report files not committed
The generated report files SHALL be written outside version control so that they are never committed with the source code, while the code that produces them remains tracked.

#### Scenario: Reports excluded from git
- **WHEN** reports are generated in the configured output directory
- **THEN** the directory is excluded by `.gitignore` and report files do not appear in commits

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
