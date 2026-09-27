## Why

On the plainest possible creation request — all six fields, no adversarial phrasing — the orchestrator sometimes answers the request itself, presenting "please confirm the following details" over a draft it never staged. It holds no write tool, so nothing reaches the database, but the user is handed a fabricated draft and the following confirmation turn finds nothing to confirm and misses its row. Measured 1 of 6 after the previous change closed the related defect in the creation specialist, and the live creation gate is red because of it.

## What Changes

- **The orchestrator stops narrating work it cannot perform.** Its routing text already says "a creation request is never yours to handle", and it still answers roughly one turn in six. The instruction is restated so that *holding no tool* is presented as the reason the work must be handed off, rather than as a fact about its own toolset.
- **Activating a skill is not performing it.** The create skill's step 5 tells the reader to "present the draft as a structured summary ... and explicitly ask the user to confirm" — a step executable in pure prose, with no tool call that would force reality to diverge from the text. That is how the orchestrator produces a plausible draft with `staged=False`. The rule becomes explicit: guidance reaching the agent that activated a skill does not make that agent the performer of the steps the skill describes.
- **No skill-text change.** Routing language is already banned in skill text by `The_create_skill_does_not_steer_routing`, and adding it there would contradict an existing decision. The guidance is corrected in the prompt the orchestrator actually reads.
- **Verification pools samples.** A 1-in-6 defect cannot be distinguished from unchanged by a single 6-run pass, so the confirming measurement is pooled rather than one sample, and the check stays at the inherited 1.0 rather than gaining a floor that would hide it.

## Capabilities

### New Capabilities

None. Both requirements below tighten existing specs; the defect is a prompt-behaviour gap, not a missing capability.

### Modified Capabilities

- `agent-workflow-handoff`: the orchestrator's obligation on a write-path request currently states only that it holds no write tool. That is necessary but not sufficient — holding no tool did not stop it presenting a draft. The requirement is extended to cover work the orchestrator presents without obtaining it from a tool.
- `skill-framework`: `Skills guide without granting new capabilities` covers tool access but not authorship of the procedure. A skill's steps are performed by the agent owning the capability they act on, not by the agent that activated the skill.

## Impact

- `AgentGraphComposer.HandoffInstructions` — the orchestrator's routing text, selected by entry point at composition time.
- `RequisitionCreationSpecialist` / `SkillActivationSpecialist` action blocks — read only by the agent that owns the capability, so the orchestrator's copy is the one that changes.
- `scripts/live-creation-gate.sh` — the measurement protocol, not its checks. `no-handoff-recorded`, `unexpected-handoff`, and `missed-a-required-row` already detect this defect and are held at 1.0; no floor moves.
- `data/skills/create-purchase-requisition.md` — read for evidence, deliberately unchanged.

**Out of scope:** the `AnswerAgent` report field, which the previous change recorded as open. The gate already detects this defect through the absence of a handoff, so the field is not needed to observe or fix it, and a report-schema delta does not belong in a prompt change.
