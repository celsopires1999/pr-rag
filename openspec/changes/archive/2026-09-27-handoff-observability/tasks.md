## 1. Confirm the shipped observability is fully specified

- [x] 1.1 Verify `EntryAgent` and `Handoffs` on `RagQueryReport` are covered by `Routed_turns_are_attributable_to_its_entry_agent_and_its_handoffs`, and add any scenario the code supports but the spec does not state
- [x] 1.2 Verify the per-agent observation point is covered by `A_handoff_is_observed_where_the_emitting_agent_is_still_known`, including the case where the target refuses after the call is emitted
- [x] 1.3 Confirm `HandoffToolName.ResolveTarget` throws on a recognised name with an out-of-range position, and that a test exercises the throw rather than only the successful resolutions
- [x] 1.4 Confirm `Handoffs` is cleared at the start of each turn, so a second turn in one session cannot inherit the previous turn's delegations

## 2. Close the creation agent's staging shortfall

> Retargeted after the 2.1 baseline refuted the original attribution. These tasks
> were written against the orchestrator declining to hand off; the baseline showed
> 24 of 24 turns recording `prrag.orchestrator->prrag.creation`, so the orchestrator
> was never the agent declining. See `design.md` Context.

- [x] 2.1 Reproduce the shortfall on the live gate and record the measured rate before changing anything, so the fix is attributed rather than assumed
- [x] 2.2 State in the creation agent's action block that a complete request is what stages a draft, and that "skip the confirmation" is a preference about the confirmation step rather than a claim a draft already exists
- [x] 2.3 Keep the instruction in `RequisitionCreationSpecialist`'s own action block, the only place the declining agent reads it; an equivalent instruction in the orchestrator's block would be a no-op against a measured failure
- [x] 2.4 Add a regression assertion that the creation agent's action block names the complete-request shape, so the guidance cannot silently drop out of the prompt again
- [x] 2.5 Re-measure at N=6 and update the `no-draft-staged` floor to cite the new pooled sample, keeping the target at 1.0 even if the measurement does not reach it

## 3. Guard the live gate mechanism

- [x] 3.1 Add a standalone test for `scripts/lib/gate_report.py` covering a clean run, a rate breach, an invariant violation, an unevaluated turn, and a mixed-severity run
- [x] 3.2 Assert in that test that a clean run prints per-probe turn counts, so "checked and clean" is distinguishable from "nothing was examined"
- [x] 3.3 Assert in that test that a run of unevaluated turns exits non-zero and does not print a pass
- [x] 3.4 Assert that a probe named in `--expect-probes` but absent from the observations fails the run
- [x] 3.5 Confirm the creation gate's `MIN_RATES` comment cites a pooled sample and that no floor was set to match its most recent result

## 4. Documentation consistency

- [x] 4.1 Update the `AGENTS.md` gotcha on the superseded requirement so it points at the new obligation rather than describing routing as log-only
- [x] 4.2 Re-read the `write-path-isolation` archive's specs and confirm nothing else contradicts shipped behaviour
- [x] 4.3 Record the measured rate and the open target for `no-draft-staged` wherever the shortfall is tracked, so an accepted rate is not later read as a fixed defect
