## 1. Prerequisite

- [ ] 1.1 Archive `agent-workflow-handoff`, or apply it first, so `specialist-capabilities` carries "A capability may be bound to its own agent". This change supersedes that requirement for the write path — the parent's own scenario says a not-yet-extracted capability keeps its tools on the orchestrator, which stops being true here — and that requirement only enters the main specs on archive. Archiving first means the supersession is stated against a baseline that already says it. Note the mechanical constraint does not apply to this delta: it modifies "Capability tool partition is verifiable", which is already in the main specs, so it validates either way
- [ ] 1.2 Author `scripts/live-creation-gate.sh`. It does not exist yet — the probes for the write path have been run ad hoc, so the regression suite for this change has to be written before it can be run. Model it on `scripts/live-answer-hygiene.sh`, including the parts that matter for an outage rather than a hygiene fault: fail on a non-2xx, fail on an empty answer, and do not report a turn as clean when no report was written to check. A probe that cannot tell a working system from a dead provider is worse than none
- [ ] 1.3 Run it against the current code and record the baseline, so a later failure can be attributed to the extraction rather than assumed to predate it

## 2. Write-attempt observability

Do this before the move: the gate is verified from the report, so the report has to carry the facts first.

- [ ] 2.1 Add the write facts to the report — draft staged, confirmation recorded, row written — as three independent booleans, and set them in `ChatService` from the turn's tool calls rather than from the draft state
- [ ] 2.2 Name the write tool set explicitly, mirroring `ToolNames.ReadOnly`, so a write attempt is not inferred as "any tool that is not a read"
- [ ] 2.3 Cover the three cases in `RagObservabilityReportTests`: a refused write, a performed write, and a turn that claimed a creation while calling no write tool
- [ ] 2.4 Mutation-check the refused-write case by making the flag a function of the row count, which collapses refused and absent, and confirm the test fails

## 3. Bind the creation capability

- [ ] 3.1 Set `AgentSlug = AgentIds.Creation` on `RequisitionCreationSpecialist` and drop the "not yet bound" note from the constant's doc comment
- [ ] 3.2 Confirm the composer requires no change: it already builds an agent per distinct slug and throws on a duplicate
- [ ] 3.3 Update `AgentFrameworkLayeringTests` to assert full per-unit ownership — every unit's tools on exactly one agent, and the orchestrator holding no tool that writes — and keep the assertion written as an explicit leftover set rather than relaxing it
- [ ] 3.4 Give the orchestrator's action block a handoff trigger for creation requests, and remove its write bullets now that it cannot call them
- [ ] 3.5 Check the creation unit's action block names no retrieval tool, and extend the shipped-skill style guard to cover it — this is the failure that already happened once, where skill text told the orchestrator to call a tool it did not own

## 4. Deterministic re-entry on a confirmation turn

- [ ] 4.1 Select the entry point at composition time from the draft's recorded state, so a turn with a pending draft enters at the creation agent and a normal turn still enters at the orchestrator
- [ ] 4.2 Assert both entry points in a test, including that a pending draft does not make the orchestrator the entry point, so the state-dependence cannot regress silently
- [ ] 4.3 Verify an unrelated request during a pending confirmation writes nothing and leaves the draft staged
- [ ] 4.4 Verify a user claiming a confirmation for a draft never staged in this session writes nothing, and that the answer does not assert a creation
- [ ] 4.5 Decide and record the skill-activation behaviour while a draft is pending, since the orchestrator is not on that path; state the outcome in the creation action block rather than letting the model discover it

## 5. Verification

- [ ] 5.1 `dotnet build backend/PrRag.sln` and the full suite
- [ ] 5.2 Re-run the gate from 1.2: draft stage writes nothing, the confirmed happy path writes exactly one row, all-fields-without-confirmation writes nothing, and a prior-session confirmation claim writes nothing
- [ ] 5.3 Re-run `scripts/live-answer-hygiene.sh`, since the creation agent now has a longer instruction surface and the two gates share a model
- [ ] 5.4 Confirm the report for a refused creation shows a write attempt with no row, which is what the 2.3 test asserts in process
