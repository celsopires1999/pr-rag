## Context

The observability code for this change is already shipped (commit `9111e69`) and its specs are not. `RagQueryReport` carries `EntryAgent` and `Handoffs`; `AgentTurnContext` records them; `HandoffRecordingChatClient` observes handoff calls per agent; `HandoffToolName` resolves the framework's positional `handoff_to_N` convention. All 127 tests pass and the live gates exercise it.

The design question is therefore not "how to observe a handoff" but two narrower ones: how to supersede a spec requirement that forbids the very fields now shipped, and how to close the routing defect that the new observability exposed.

That defect is specific. On the adversarial probe — all six fields supplied, plus "skip the confirmation" — the orchestrator replies that it holds no requisition draft and stops, instead of handing off to the creation agent. Measured at 7 of 18 turns. Nothing is written and nothing false is claimed, so it is safe; it is a usefulness and routing shortfall. A fake model always routes, so no unit test can see it and only the live gate measures it.

Constraint: the orchestrator holds no write tool, so a turn needing one can only be satisfied by a handoff. The orchestrator's failure to hand off is therefore the whole failure, with no fallback path.

## Goals / Non-Goals

**Goals:**
- Supersede the "delegation is observable without changing the observability report" requirement, and retire the scenario asserting write-path isolation is not delivered, since it is.
- Specify `EntryAgent`/`Handoffs` as report requirements and the observation point that makes them possible.
- Specify the live gate's classification, its floors, and its clean-run reporting.
- Close the orchestrator hand-off shortfall, and raise the gate floor as it closes.

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

**Close the routing defect in the orchestrator's instructions, not in the creation agent's.** The orchestrator is the only agent on that path and the only one that can decide to hand off, so guidance placed in the creation agent's action block would never be read. The existing instruction already names confirmation, "skip the confirmation", complete-field, and draft-correction requests; this narrows it to the remaining shape, where the model treats "I already gave you everything" as a reason not to engage.

Alternative considered: route on the question rather than on the model's judgement. Rejected — the entry point is already state-driven for a pending draft, and extending it to infer intent from text would reintroduce exactly the prompt-coupled routing the state-based entry point removed.

**Keep the check a rate after the fix.** A narrower prompt reduces the rate of the failure; it does not make the turn deterministic, and the adversarial phrasing is deliberately hostile. Holding it at one would mean the gate is red until a probabilistic model becomes deterministic.

## Risks / Trade-offs

- [The superseded requirement is the one a future reader consults before adding report fields] → the replacement states the obligation positively and names the diagnostic defect it exists for, so the reasoning is not lost.
- [A prompt change does not fix a model behaviour; the measured rate may not move] → the gate reports the rate either way, and a floor that does not improve is visible as a non-improvement rather than as a pass.
- [Raising the floor after the fix makes the gate red if behaviour regresses] → intended, and the floor cites the sample that justified it.
- [Two extra report fields grow the report] → both are small and populated on every turn; the empty-list and entry-agent cases keep single-agent turns unexceptional.
- [The live gate is the only test for the routing defect] → a fake model cannot reproduce it, so this is inherent; the rate report is the mitigation.

## Migration Plan

No migration. The report fields are additive and already shipped; report consumers read named fields. Superseding a spec requirement and raising a gate floor need no deploy.

## Open Questions

- What floor should `no-draft-staged` settle at once the routing fix lands? It is held at 0.45 against a measured 0.58. If the fix moves the measurement to 1.0 the floor should be raised to 1.0; if it plateaus lower, the target is still 1.0 and the shortfall stays documented as open.
