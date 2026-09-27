## Why

The archived `write-path-isolation` change shipped handoff and entry observability, but no spec was updated to match, and one existing spec now asserts the opposite of the code. `agent-workflow-handoff` requires that the system SHALL **NOT** add fields to the RAG observability report, while `RagQueryReport` now carries `EntryAgent` and `Handoffs`. That contradiction is live: the next reader of the spec is told the report is unchanged when it is not.

The superseded requirement was not wrong when written. It was adopted to keep report consumers unaffected by the multi-agent graph, and the change that invalidated it is a defect: a handoff is a workflow edge, not an application tool, so it contributed no tool call, and the framework names it by participant *position* (`handoff_to_1`) which nothing on the composed agent maps back to a capability. Two opposite failures therefore produced the same report — the entry agent answering a creation request it holds no write tool for, and it routing correctly to creation which then gave a bad answer. Neither was diagnosable from the report.

## What Changes

- **Replace** the `agent-workflow-handoff` requirement "Delegation is observable without changing the observability report" with one that requires the report to carry the entry agent and the handoffs, and that a handoff is resolved to a capability rather than left positional.
- **Retire** the scenario "The transitional topology is not presented as least privilege", which asserts write-path isolation is not delivered. It is delivered: the orchestrator holds no write tool.
- **Add** an `EntryAgent` and `Handoffs` requirement to `rag-observability-report`, covering that a routed turn is attributable to the agent that entered it and to the capability each handoff targeted.
- **Add** a `live-gate-reporting` capability specifying that live gate checks are classified as invariants, rate-limited capabilities, or unevaluated turns, and that the three are never averaged together.
- **Close the open routing defect.** On roughly half the turns of the adversarial skip-the-confirmation probe (10 of 18 measured, pooled), the creation agent reaches the turn and declines to stage, answering that it has no draft awaiting confirmation — and on one turn in six invents one. Nothing is written and nothing false is claimed on two of the three shapes, so it is a rate rather than an invariant — but the observability added in `write-path-isolation` is what made it measurable, and the defect is the same class as the wrong-blocker answers already fixed once. This was first written up as the orchestrator declining to hand off; the first live baseline refuted that, because every measured turn recorded the handoff. The prompt change lands on the creation agent's action block.

## Capabilities

### New Capabilities
- `live-gate-reporting`: classification of live gate checks into invariant, rate, and unevaluated turn, and the rules that keep a stochastic result from being reported as a pass.

### Modified Capabilities
- `agent-workflow-handoff`: the requirement that delegation adds no report fields is superseded; routing and handoff must be attributable in the report, and a handoff must resolve to a capability rather than a participant position.
- `rag-observability-report`: the report additionally records the entry agent and the handoffs taken, so a routed turn is attributable.

## Impact

**Already shipped, spec catching up** (commit `9111e69`, no code change expected):
- `RagQueryReport` gains `EntryAgent` and `Handoffs`; `RagHandoff` is new.
- `AgentTurnContext` gains `EntryAgent`, `Handoffs`, and `RecordHandoff`.
- `HandoffRecordingChatClient` and `HandoffToolName` are new.

**Requires code change:**
- `RequisitionCreationSpecialist` instructions, to stop the creation agent reading a complete request as a draft already awaiting it.
- `scripts/live-creation-gate.sh`, which already asserts the behaviour; its floor should be raised toward 1.0 as the fix lands rather than relaxed to match a measurement.

**Not in scope:** the `ToolNames.ReadOnly` read/write split and the read/write attempt facts are already specified by the archived change and are not restated here.
