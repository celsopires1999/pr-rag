## 1. Record the field

- [x] 1.1 Add `AnswerAgent` to `RagQueryReport` beside `EntryAgent` and `Handoffs`, nullable, with a doc comment stating that the author is the last agent to emit non-empty text and that null means no agent produced text
- [x] 1.2 Add `AnswerAgent` to `AgentTurnContext` with a `RecordAnswerAuthor` method mirroring `RecordHandoff`'s signature, and reset it in `Begin` next to `Handoffs.Clear()` so a turn cannot inherit an earlier turn's author
- [x] 1.3 Copy it into the report in `ChatService` alongside the two routing facts, with a comment saying why the three together are what separate an unrouted answer from a badly handled delegation
- [x] 1.4 Do not default it to the entry agent anywhere, including on the failure path, because a defaulted value is indistinguishable from a real answer and is the field's only failure signal

## 2. Rename the decorator and extend it

- [x] 2.1 Rename `HandoffRecordingChatClient` to a name covering both facts it records, updating the file, the single call site in `AgentGraphComposer.Recording`, and the class doc comment so it says it attributes both the handoffs and the answer's author
- [x] 2.2 Record the author in `GetResponseAsync` when `response.Text` is non-empty after trimming, and leave a tool-call-only response unrecorded
- [x] 2.3 Accumulate `ChatResponseUpdate.Text` across the streaming path and record the author whenever the accumulated text is non-empty, relying on last-write-wins across the turn's decorator instances rather than on any coordination between them
- [x] 2.4 Leave the handoff recording, `HandoffToolName`, and the participant order untouched, since this change adds a fact and does not move a graph edge

## 3. Tests

- [x] 3.1 A delegated turn records the target as the answer agent, using `FakeChatClient.AnswerByAgent` so the two agents' text is distinguishable, and asserting the recorded slug rather than the answer text
- [x] 3.2 A turn answered without delegating records the entry agent as the author
- [x] 3.3 A response carrying only a function call does not become the author, so a mid-loop tool call cannot claim the answer
- [x] 3.4 A streamed multi-agent turn records the last agent that emitted text, driven through `ChatService.StreamAsync` so the non-streaming path is not what is under test
- [x] 3.5 A run that yields no text records no author, and the report is still written — assert the report file exists and reads as an unevaluated turn rather than as an empty answer
- [x] 3.6 A second turn in a reused context records its own author, not the first turn's
- [x] 3.7 Assert the field is genuinely consistent with the answer: on a delegated turn, the recorded author is the agent whose `AnswerByAgent` text equals `response.Answer`, so a future graph change that lets a different agent's text survive fails here rather than making the field quietly wrong

## 4. Make it checkable

- [x] 4.1 Add a `HARD` check to `scripts/live-creation-gate.sh` on the probes that expect a delegation: the recorded answer agent must be the target, so an entry agent authoring an answer on a probe that expected a handoff fails on a single occurrence and acquires no floor
- [x] 4.2 Have the check treat a report with no `answerAgent` as unevaluated rather than as a pass, consistent with the existing `INFRA` handling, so a run that cannot attribute an answer never counts as clean
- [x] 4.3 Update the gate script's `OPEN and NOT this floor` comment, which records the 1-in-19 orchestrator defect as unmeasurable, to say what this field now measures and how the rate should be re-measured once the check exists
- [x] 4.4 Update the `AGENTS.md` gotcha to close the "what the report still cannot do is name the agent that authored the answer" sentence, and to say that the field measures the last agent to emit text rather than the furthest handoff target

## 5. Verify

- [x] 5.1 Run the full .NET suite and confirm the report DTO change did not break the existing routing assertions, which read `EntryAgent` and `Handoffs` and are unaffected by an added field
- [x] 5.2 Run `python3 -m unittest discover -s scripts/lib -t scripts/lib` to confirm the gate summariser still passes, since the new check is classified in the same file
- [x] 5.3 Run `scripts/live-creation-gate.sh 6` against the demo API and record the result honestly, expecting an occasional failure at the known 1-in-19 rate and reporting it as the gate working rather than as a regression in this change
- [x] 5.4 Confirm `openspec validate --all` passes after the delta is synced
