## Context

The observability code for this change is already shipped (commit `9111e69`) and its specs are not. `RagQueryReport` carries `EntryAgent` and `Handoffs`; `AgentTurnContext` records them; `HandoffRecordingChatClient` observes handoff calls per agent; `HandoffToolName` resolves the framework's positional `handoff_to_N` convention. All 127 tests pass and the live gates exercise it.

The design question is therefore not "how to observe a handoff" but two narrower ones: how to supersede a spec requirement that forbids the very fields now shipped, and how to close the routing defect that the new observability exposed.

That defect is specific. On the adversarial probe — all six fields supplied, plus "skip the confirmation" — the turn reaches the creation agent and that agent declines to stage, replying that it has no draft awaiting confirmation (and, on one turn in six, inventing one). Measured at 10 of 18 turns, pooled over three samples of 6. Nothing is written and nothing false is claimed on two of the three shapes, so it is safe; it is a usefulness shortfall. A fake model always routes, so no unit test can see it and only the live gate measures it.

**This was originally attributed to the orchestrator, and the attribution was wrong.** The first baseline run of the shipped observability refuted it: all 6 adversarial turns recorded `prrag.orchestrator->prrag.creation`, and 24 of 24 turns across the run recorded a handoff, so the orchestrator was never the one declining. The reason the report could not settle this is itself part of the defect: `EntryAgent` and `Handoffs` say which agent the turn *entered* and which agent it *reached*, but not which agent *authored the answer*. A turn where the entry agent answered and never handed off therefore looks identical to one where the entry agent handed off and the target then answered as though nothing were pending. `The_answer_to_a_handed_off_turn_is_the_targets_own_text` pins which of the two it is — the target's text reaches the caller — but the report still cannot attribute an answer, and that gap stays open (see Open Questions).

Constraint: the orchestrator holds no write tool, so a turn needing one can only be satisfied by a handoff. The handoff is therefore never the failure, and the agent that can actually decline is the one holding the write tools.

## Goals / Non-Goals

**Goals:**
- Supersede the "delegation is observable without changing the observability report" requirement, and retire the scenario asserting write-path isolation is not delivered, since it is.
- Specify `EntryAgent`/`Handoffs` as report requirements and the observation point that makes them possible.
- Specify the live gate's classification, its floors, and its clean-run reporting.
- Close the creation agent's hand-off shortfall, and raise the gate floor as it closes.

**Non-Goals:**
- Re-specifying the read/write attempt facts and the read-tool split; the archived change covers those.
- Changing the report file format, its location, or its consumers beyond the two added fields.
- Making the model deterministic. A prompt change narrows the failure; it does not eliminate it, which is why the check stays a rate.

## Decisions

**Supersede the requirement rather than delete it.** The old requirement was correct when written — it kept report consumers unaffected by the multi-agent graph. Deleting it would erase the reason the design changed, and the next person to propose report fields would hit the same reasoning. The replacement states the obligation and names what the report is for.

Alternative considered: keep the requirement and move routing to a separate channel. Rejected — a handoff is a workflow edge, so it produces no application tool call, and the framework names it by participant position. There is no second channel that carries the fact.

**Observe handoffs with a per-agent chat-client decorator, not a handler.** The decorator is the only layer that knows which agent emitted a call, and it must sit innermost so it sees the call before the function-invocation layer dispatches it. A tool handler is too late, and a handler would not run at all for an edge that dispatches outside the tool layer.

Alternative considered: infer the handoff from the tool call list. Rejected — it is exactly the inference that failed, since a handoff contributes no `RagToolCall`.

**Resolve the positional name in one place and throw out of range.** `HandoffToolName` is the only component that knows the positional convention, so a test and the production signal cannot disagree about which number means which capability. It throws on a recognised name with an out-of-range position, because that is the graph and the convention diverging, and any handoff recorded for the turn would otherwise be attributed to the wrong capability — a wrong attribution is worse than a failed turn.

**Close the defect in the creation agent's instructions, not the orchestrator's.** The turn arrives at the creation agent by handoff on every measured turn, so the orchestrator has no failing behaviour to correct and an instruction placed there would be a no-op. The creation agent is the only one that can decline, and its action block is the only place the correction is read. The failure is not that the agent lacks the tools; the `forged` probe passes 6 of 6 with that same agent correctly reporting no draft pending. It is that when the same turn carries every field, the model resolves the completeness of the request into "a draft must therefore be awaiting me" and stops, which is backwards: a complete request is what *stages* a draft, and "skip the confirmation" describes the user's preference for the confirmation step, not a claim that the draft exists. The fix names that distinction.

The original design reasoned the opposite way — that guidance in the creation agent's action block "would never be read" because the orchestrator is the only agent that can decide to hand off. That reasoning is sound and its conclusion does not follow: the orchestrator deciding to hand off is not the same as the target acting on the turn, and only the latter was ever failing. The live baseline is what separated them, which is the argument for having shipped the observability first.

Alternative considered: also narrow the orchestrator's routing instruction, on the theory that it is harmless. Rejected — it is a prompt change to an agent with no measured failure, which adds surface and a second thing to attribute if the rate does not move.

Alternative considered: route on the question rather than on the model's judgement. Rejected — the entry point is already state-driven for a pending draft, and extending it to infer intent from text would reintroduce exactly the prompt-coupled routing the state-based entry point removed.

**Keep the check a rate after the fix.** A narrower prompt reduces the rate of the failure; it does not make the turn deterministic, and the adversarial phrasing is deliberately hostile. Holding it at one would mean the gate is red until a probabilistic model becomes deterministic.

## Risks / Trade-offs

- [The superseded requirement is the one a future reader consults before adding report fields] → the replacement states the obligation positively and names the diagnostic defect it exists for, so the reasoning is not lost.
- [A prompt change does not fix a model behaviour; the measured rate may not move] → the gate reports the rate either way, and a floor that does not improve is visible as a non-improvement rather than as a pass.
- [Raising the floor after the fix makes the gate red if behaviour regresses] → intended, and the floor cites the sample that justified it.
- [Two extra report fields grow the report] → both are small and populated on every turn; the empty-list and entry-agent cases keep single-agent turns unexceptional.
- [The live gate is the only test for the routing defect] → a fake model cannot reproduce it, so this is inherent; the rate report is the mitigation.
- [The report still cannot say which agent authored the answer] → known and accepted for this change; `EntryAgent` and `Handoffs` localise the turn to a chain, and `The_answer_to_a_handed_off_turn_is_the_targets_own_text` pins the rest in code. A report that attributed an answer would close the gap properly, and is a separate change with its own spec delta.

## Migration Plan

No migration. The report fields are additive and already shipped; report consumers read named fields. Superseding a spec requirement and raising a gate floor need no deploy.

## Open Questions

- What floor should `no-draft-staged` settle at once the fix lands? Settled. It moved to 1.00 (6 of 6) after the prompt change, so the floor is raised to 1.0, citing the post-fix sample. The pre-fix history is 0.58, 0.50, 0.50 — 10 of 18 pooled — and is kept above so the reason for the change is legible.
- **Open, and newly visible.** The same re-measure surfaced a defect this change does not fix: on the `stage` probe the orchestrator sometimes answers a creation request itself, with no handoff and no tool call, presenting "please confirm the following details" over a draft it never staged. Measured 1 of 6 post-fix, and it cascades — the following confirmation turn then finds no draft and misses its row. It is a fabricated fact rather than a routing shortfall, so it is not a rate to be floored, and `MIN_RATES` leaves it visible at the inherited 1.0 instead of tuning around it. This is the orchestrator-side failure the original design hypothesised, on a different probe and at roughly a sixth the rate; the entry prompt is the place to address it, which is outside what this change set out to do.
- Should `RagQueryReport` carry the agent that authored the answer? Left open deliberately. It would have made this misdiagnosis impossible and is the natural next report field, but it is new capability rather than a fix to this change's stated obligation, so it needs its own spec delta.
