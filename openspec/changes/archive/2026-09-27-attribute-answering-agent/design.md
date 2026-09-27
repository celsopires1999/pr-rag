## Context

The report has two routing facts and no authorship. `EntryAgent` is the deterministic composition decision, `Handoffs` is every delegation taken, and between them they describe a *chain*. Neither names who wrote the sentence the user received, so these two turns are indistinguishable:

- the orchestrator answered a creation request itself, over a draft it never staged
- the orchestrator routed correctly, and the creation specialist then gave a bad answer

That ambiguity is not hypothetical. The previous two changes both hit it: a recorded claim that the orchestrator was declining to hand off on roughly half the turns survived two changes' worth of work, and the observability shipped to settle it refuted that on its first live run — 24 of 24 turns had recorded the handoff, and the agent declining was `RequisitionCreationSpecialist`. The report could not arbitrate because both stories fit it.

The most recent change closed a real 1-in-19 defect on static reasoning, because the gate could see the *absence* of a handoff but not who had answered instead. This change supplies that.

## Goals / Non-Goals

**Goals:**
- Record the agent that authored the turn's answer, as a capability slug, on every turn including a failed one.
- Make the field incapable of disagreeing with the answer it describes.
- Close the open item recorded in `AGENTS.md` and in the previous change's archived spec.
- Give the live creation gate a check it can run: a probe that expects a delegation should be able to assert the answer came from the target.

**Non-Goals:**
- No prompt change. The previous change's prompt edit is not revisited, and no instruction text is touched.
- No new routing facts. The field is read-only observability; it never influences composition, tools, or what the model is told.
- Not a "who was furthest from the entry point" signal. That is a different question and the handoff chain already answers it.
- No change to the checks already in either live gate.

## Decisions

**Define the author as the last agent to emit non-empty text, and treat that as equivalent to "who wrote the answer" rather than as an approximation of it.** The run's `Text` *is* the last text any agent emitted, so the two cannot drift: if an agent produced text that was then discarded, that discarded text was not the answer either. This is why "last emitter" is the right definition and not merely a convenient one — it is the same thing by construction. The alternative, deepest handoff target, is stable when the entry agent resumes and speaks but wrong whenever the target does not produce the final text, and it would report a chain position as though it were an author.

A response that emits only a function call contributes nothing. That case is the common one mid-loop, and letting it claim authorship would attribute the answer to whichever agent happened to call a tool last.

**Record it in the per-agent decorator, for the same reason handoffs are recorded there.** The composer binds a `DelegatingChatClient` per agent and it sits innermost, so it sees each agent's own model traffic. It is the only place that knows which agent is speaking; a tool handler would be downstream of the decision and a session-level observer would be downstream of the answer. Last write wins across the turn's decorator instances, because they all write one field on the shared `AgentTurnContext` — so no coordination is needed, and ordering follows from the run itself.

**Let a turn with no text report no author.** The dead-provider path completes a run with empty text and `/api/chat` maps it to 502, and the report is written before that throw. Today that report reads like a quiet turn. With `AnswerAgent` null it reads as a turn that reached no author at all, which is the fact an operator needs. This is the reason the field is nullable rather than defaulting to the entry agent: a defaulted value would be indistinguishable from a real answer and would quietly destroy the field's only failure signal.

**Rename the class.** `HandoffRecordingChatClient` would be the only place `AnswerAgent` comes from, under a name naming the other fact. It is `internal` with two code references, so the cost is a file rename, one call site, and the doc references. A class whose name misdescribes where the report's most load-bearing field comes from is the kind of thing that sends the next reader looking in the wrong file.

**State the interaction with the existing fields, because the combination is the whole point.** Three shapes are now separable and none was before:

| `EntryAgent` | `Handoffs` | `AnswerAgent` | reading |
|---|---|---|---|
| = answer | empty | equal to entry | answered in place, no delegation |
| ≠ answer | non-empty | target | delegated; the target is on the hook for the text |
| ≠ answer | non-empty | equal to entry | delegated, then the entry agent spoke again — the answer is a closing remark, and the chain explains why |
| any | any | null | no agent produced text |

The third row is the one that cannot occur today, because `The_answer_to_a_handed_off_turn_is_the_targets_own_text` would fail if the entry agent's resumed text became the answer. It is in the table so that if it ever does occur, the report says so instead of the field quietly absorbing it.

**Verify by test, not by gate.** This is the part that differs from the change before it. `FakeChatClient.AnswerByAgent` already exists and gives distinguishable text per agent, so every row above is a deterministic in-process test: delegated turn, non-delegated turn, tool-call-only response, and the empty run. The previous change could not do this — a fake model does not improvise a draft summary, so its fix rested on static reasoning and its 18 clean turns proved nothing. This one is observable before it ships.

## Risks / Trade-offs

- [A HARD gate check on answer authorship will make the creation gate red on the real 1-in-19 rate] → accepted, and it is the point. A gate that cannot see this failure is why the rate took three changes to establish. It is recorded as an invariant because the answer is a fabricated routing claim, not a capability shortfall, and it must not acquire a floor.
- [A streamed answer arrives in pieces, so authorship could be recorded against a partial response] → authorship is only recorded when accumulated text is non-empty, and last-write-wins makes the final value the last agent that had text. Covered by a streaming test that does not use the non-streaming path.
- [Renaming a class for a second reason creates a commit that looks like churn] → the rename ships with the field that justifies it, in the same commit, and the alternative is a class named for one of the two facts it records.
- [`AnswerAgent` becomes a field operators trust and then route decisions on] → it is written by a decorator and read by nothing in the application. Stated here so that a future change which makes routing depend on it is a deliberate act rather than a drift.
- [A null `AnswerAgent` is a new state that existing report consumers may not handle] → reports are read by the live gates and by tests, both in-repo, and the gates treat an absent field as unevaluated rather than as a pass.
