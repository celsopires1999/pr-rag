## 1. Establish the baseline before changing anything

- [x] 1.1 Re-run `scripts/live-creation-gate.sh` against the demo API and confirm the defect is still present, recording the measured rate and the answer text, so the fix is attributed to a reproduced failure rather than to the previous change's single observation
      **Result: not reproduced.** Three pooled samples (18 fresh turns) all clean; 1 failure in 24 `stage` turns including the earlier recorded observation, so the true rate is ~1/24, not 1/6. See "Baseline" in the design for what this does to verification.
- [x] 1.2 Confirm which agent authored the failing answer, using the fact that `The_answer_to_a_handed_off_turn_is_the_targets_own_text` pins: an unrouted turn's answer is the entry agent's own text, so a turn with no handoff recorded and a draft in the answer is the orchestrator speaking
      **Result: confirmed from the record.** The failing turn was `entry=prrag.orchestrator` with `Handoffs` empty, and every clean turn in the 18 measured records the handoff `prrag.orchestrator->prrag.creation`. By the pinned invariant the authored text is the entry agent's own.
- [x] 1.3 Read `data/skills/create-purchase-requisition.md` step 5 and confirm it names no tool for presenting the draft, which is what makes the step executable in prose by an agent holding no write tool
      **Result: confirmed.** Step 5 is a six-line field template ending "and explicitly ask the user to confirm", with no tool named anywhere in the step. Step 8, by contrast, does name `confirm_requisition_draft`. Step 5 is the prose-renderable one, and it is the one that runs first.

## 2. Correct the orchestrator's instructions

- [x] 2.1 State in the orchestrator's `ActionBlock` (the `SkillActivationSpecialist` unit's block) that a step whose result can only come from a tool the agent does not hold is not that agent's to perform, phrased as a capability rule rather than as a routing instruction so it is not routing language
      Added a bullet to `SkillActivationSpecialist.ActionBlock`: "A step you cannot perform is not yours to carry out". It is phrased about capability and about activation not conferring authorship, never about which agent handles a request.
- [x] 2.2 Add the fabrication guard to the same block: never present a draft, summary, or confirmation request that was not returned by a tool call, placed beside the `activate_skill` bullet because the misreading begins at activation
      Added "Never present an artifact that no tool returned" directly after the activation bullet.
- [x] 2.3 Confirm the wording does not conflict with `The_create_skill_does_not_steer_routing`, which bans routing phrases from skill text, and that no equivalent phrase was added to `data/skills/create-purchase-requisition.md`
      Both clean. The banned-phrase list in that test is applied to the skill file, not to action blocks, and the skill file is byte-identical (`git status data/skills/` empty). The new bullets also name no foreign tool, so `No_units_action_block_names_a_tool_it_does_not_own` still passes — the wording says "a draft", never `create_requisition_draft`, because that test substring-matches the registered tool names.
- [x] 2.4 Leave `AgentGraphComposer.HandoffInstructions` unchanged, since the existing text is not wrong but outranked by guidance injected later in the run, and duplicating the rule there would create a second copy that can drift
      Unchanged, confirmed by `git status`.

## 3. Guard the prompt and the context assembly

- [x] 3.1 Add a prompt assertion that the orchestrator's action block names both the absent-tool rule and the fabrication guard, so the guidance cannot silently drop out of the prompt again
      New `backend/tests/PrRag.Tests/OrchestratorActivationPromptTests.cs`, 5 tests over the whitespace-collapsed `SkillActivationSpecialist.ActionBlock`, following `CreationStagingPromptTests`. The fifth asserts the block names no tool the unit is not offered, so a future edit that reaches for a tool name fails here with a readable message instead of tripping the layering test.
- [x] 3.2 Add a test that a run carrying activated skill guidance still carries the orchestrator's own instructions, asserting against `LastMessages` rather than `LastPrompt`, so a regression in context assembly is caught rather than re-diagnosed from a gate failure
      `Activated_guidance_does_not_displace_the_orchestrator_instructions` in `SkillFrameworkTests`. Two turns in one session: turn 1 activates, turn 2 carries the guidance. Asserts the guidance is present as a run-level `ChatRole.System` message and that `PromptForAgent(ToolNames.ActivateSkill)` still holds both new rules. It asserts both channels are loaded, not that one beats the other — the defect was the model preferring the later message, not a channel replacing another, so only the loading is deterministic.
- [x] 3.3 Run the full suite and confirm `AgentFrameworkLayeringTests` and `ShippedSkillTextTests` still pass, since both read the text being edited
      155 passed, 0 failed (was 149). Both of those classes pass; the layering substring test was the one real hazard, since it matches registered tool names anywhere in the block.
- [x] 3.4 Mutation-check the new assertions by restoring the previous wording and confirming they fail, as was done for `CreationStagingPromptTests`
      Reverted `ActionBlock` to the pre-change wording and re-ran: 5 of 6 fail with "Sub-string not found". The sixth asserts an absence and correctly passes both ways. Restored and re-ran: 6 of 6 pass.

## 4. Measure

Note before starting: the baseline came back 1 in 24, not 1 in 6, so a clean 18-turn sample cannot distinguish fixed from unchanged. These tasks are worded to report the rate honestly, not to claim a verified improvement.

- [x] 4.1 Rebuild the demo API and run `scripts/live-creation-gate.sh 6`, then repeat until at least three samples are pooled, recording the pooled count alongside the one earlier observation so the reported rate is over 24 turns and not 18
      Rebuilt and verified the new prompt is in the running image before measuring — checked the UTF-16 string heap in `PrRag.Application.dll` copied out of the container, because the image contains no `strings` binary and a first check that used one reported a false negative. Three pooled samples post-fix: 24 turns, all clean, `stage` 6/6 per sample. **This does not demonstrate the fix**; see 4.4.
- [x] 4.2 Also run `scripts/live-answer-hygiene.sh`, since the orchestrator fronts retrieval turns too and this is the widest-blast-radius prompt in the system
      8 of 8 clean, `cold-search` 8/8. No regression on the entry prompt's other job.
- [x] 4.3 Correct the `OPEN and NOT this floor` comment in `scripts/live-creation-gate.sh`, which still says the defect was "Measured 1 of 6 post-fix". The pooled figure is 1 in 24, and the comment is the only place the rate is recorded, so leaving it at 1 of 6 would have the next reader calibrate to a rate twice the real one. Keep the three checks at 1.0 and add no floor, since a floor below 1.0 would legalise a fabricated draft
      Rewritten, and dropped the stale pointer to the now-archived `handoff-observability` change in favour of this change. `bash -n` clean. `MIN_RATES` deliberately left at its two tuned entries: the three orchestrator checks are unlisted and so held to 1.0 by the documented default, which is the strictest setting available and cannot pass falsely.
- [x] 4.4 Report the outcome as unverified, in both this file and the design's Baseline section. Do not write that the fix was confirmed by the gate, because a clean 18-turn sample is the expected result at this rate whether or not the prompt changed
      Reported here, in the design's Baseline section, and in the gate script comment. Pre-fix: 1 in 19 pooled `stage` turns (18 fresh clean + 1 earlier recorded failure). Post-fix: 0 in 18. The two pre-fix and post-fix samples are statistically indistinguishable, and the design records why before the change was applied rather than after.
- [x] 4.5 Update the `AGENTS.md` gotcha on skill text and agent routing with the outcome, so the rule that activated guidance does not confer authorship is recorded next to the one that a skill must not steer routing
      New gotcha bullet immediately above the existing skill-text one, so the unnamed step is read as the gap in that bullet's rules rather than as a separate concern. Records the mechanism (run-level injection reaches the activating agent, and arrives after its instructions), the measurable rate with its correction to 1 in 19, the unverified status of the fix, and why the fix went in the reading agent's action block rather than in skill text or in `HandoffInstructions`.

