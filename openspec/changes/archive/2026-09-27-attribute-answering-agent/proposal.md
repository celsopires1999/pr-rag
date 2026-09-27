## Why

The report localises a turn to a *chain*, not to an author, so two opposite failures are the same report: the entry agent answered a request it should have routed, and the entry agent routed correctly to a specialist which then answered badly. That ambiguity has already cost real time — it is why the orchestrator was blamed for a defect the creation specialist committed, and the shipped observability refuted the story on its first live run. The field to settle it is the one the report does not have.

## What Changes

- **The report names the agent that authored the answer.** A new `AnswerAgent` field, carrying the capability slug of whichever agent produced the turn's final text. Combined with the `EntryAgent` and `Handoffs` that already exist, the two failures separate: an entry agent equal to the answer agent with no handoffs is an unrouted answer, and a differing answer agent is a delegated one whose target is on the hook for the text.
- **Null is a real value.** A turn where no agent produced text — the dead-provider case that `/api/chat` answers with 502 — reports no answer agent. The report is already written before that throw, so the operator sees a turn that reached no author at all rather than an empty string that reads like a quiet turn.
- **The recording goes where the handoff recording already goes.** The per-agent `DelegatingChatClient` the composer binds is the only place that knows which agent is speaking, for the same reason it is the only place that sees a handoff before the framework takes it. The class is renamed, because it now attributes two facts rather than one and its name would otherwise say `Handoff` while the report's most-used field came from it.
- **A live check becomes possible.** With the author in the report, a probe that expects a delegation can assert the answer came from the target. That check does not exist today, and it is the check that would have caught the defect the previous change had to fix on static reasoning alone. Adding it is expected to turn the creation gate red occasionally on the real 1-in-19 rate; a gate that cannot see this failure is why the rate took three changes to pin down.

## Capabilities

### New Capabilities
None. The report already has a capability; this adds a field to it.

### Modified Capabilities
- `rag-observability-report`: a new requirement, added rather than merged into the existing routing one. `A routed turn is attributable to its entry agent and its handoffs` is about routing, and it stays correct and unchanged; answer authorship is a different question — who spoke, not who was called — and folding it into that requirement would either overstate the routing requirement or blur the two. The existing requirement's scenarios are unaffected.

## Impact

- `RagQueryReport` — new `AnswerAgent`, alongside `EntryAgent` and `Handoffs`.
- `HandoffRecordingChatClient` → renamed, and extended to record answer authorship. Internal, two code references, so the rename is cheap; it is here because the class is `internal` but the report's most load-bearing new field would otherwise come from something named after a different one.
- `AgentTurnContext` — records the author; reset per turn alongside `Handoffs`, so a turn cannot inherit an earlier turn's author.
- `ChatService` — copies it into the report, next to the two routing facts it complements.
- `scripts/live-creation-gate.sh` — one new check on probes that expect a delegation.
- **Out of scope:** no change to any prompt, and none to the checks already in either gate. The previous change's `AnswerAgent`-free open item closes; its prompt edit is not revisited.
