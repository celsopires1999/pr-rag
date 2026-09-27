## Context

The previous change closed the same class of defect in the creation specialist, and its re-measure surfaced this one on a different probe. The `stage` probe is the plainest creation request the system has — all six fields, no adversarial phrasing — and on 1 of 6 runs the orchestrator answered it itself:

```
FAIL  r3  stage  attempted=False staged=False entry=prrag.orchestrator
      RATE  no-handoff-recorded
      ... "Please confirm the following details for the purchase requis..."
```

No handoff, no tool call, and a draft summary invented from the six fields the user supplied. The `confirmed` turn then found no draft and missed its row, so one event fails three checks. Nothing reaches the database: the gate in `CreateRequisitionAsync` refuses an unconfirmed write regardless. The harm is that the user is told to confirm something that does not exist, and the refusal that follows is confusing.

The mechanism is a collision between two prompt sources. The orchestrator's `HandoffInstructions` already says *"You hold no tool that writes a requisition, so a creation request is never yours to handle."* It activates the skill, whose guidance arrives on the next turn as a run-level system message, and whose Procedure step 5 reads:

> When every field is collected and validated, present the draft as a structured summary ... and explicitly ask the user to confirm.

That step names no tool. It is executable in pure prose, from fields the user already supplied, and the orchestrator has no call that would make reality diverge from its text. So the narrower, more actionable instruction wins over the general routing sentence roughly one turn in six.

## Goals / Non-Goals

**Goals:**
- Stop the orchestrator presenting a draft, summary, or confirmation request it did not obtain from a tool call.
- State the rule as a capability discipline rather than a routing instruction, so it holds for any capability the orchestrator does not own.
- Confirm the fix with a pooled measurement, and report the result honestly if the rate does not move.

**Non-Goals:**
- Adding the `AnswerAgent` report field. The gate already detects this defect through the absent handoff, so the field is not needed to observe or fix it, and a report-schema delta does not belong in a prompt change.
- Editing `data/skills/create-purchase-requisition.md`. Routing language is banned there by `The_create_skill_does_not_steer_routing` and by the rule that routing guidance belongs in the owning unit's action block; adding it would reverse a settled decision.
- Changing any gate check. `no-handoff-recorded`, `unexpected-handoff`, and `missed-a-required-row` already detect this and are already held at 1.0.
- Making the turn deterministic. This is a prompt change against a stochastic model, which is why the verification pools samples.

## Decisions

**State the rule as "a step you cannot perform is not yours", not "hand off creation requests".** The obvious fix — strengthening `HandoffInstructions` — is the one most likely to fail, for a reason worth recording: the skill's guidance is injected as a run-level system message *after* the agent's instructions, and it is numbered and concrete where the routing sentence is general policy. A step that says "present the draft and ask for confirmation" reads as this agent's next action regardless of how firmly an earlier paragraph said otherwise. Strengthening the earlier text fights a losing position in the context ordering.

The rule that survives is not about routing at all. A step whose result can only be obtained from a tool is not performable by an agent that holds no such tool — that is a statement about capability, it generalises to every capability the orchestrator does not own, and it is not routing language, so it can live in an action block without violating the skill-text rule.

**Put it in the orchestrator's own `ActionBlock`, beside the activation bullet that causes the confusion.** The orchestrator is composed from the `SkillActivationSpecialist` unit, and its action block holds the `activate_skill` bullet that pulls it into this path. The misreading happens immediately after activation, so the correction belongs adjacent to the instruction that triggers it. This also follows the existing rule that routing guidance belongs in the owning unit's action block rather than in skill text or in prose the composer supplies.

`HandoffInstructions` stays as it is. It is not wrong — it is outranked. Its per-entry-point variation is the reason it exists, and duplicating the correction into it would put the same rule in two places that could drift.

**Add the fabrication guard to both agents' prompts.** The creation specialist's block now says "Never assert a draft you have not staged". The orchestrator commits the same fabrication from the opposite direction — it stages nothing and asserts a draft exists. Both prompts should forbid presenting a draft not obtained from a tool, so the rule reads the same wherever a turn starts.

**Verify by pooling, and keep the check at 1.0.** A 1-in-6 defect is not distinguishable from unchanged by a single 6-run pass; the previous change's own note records that both of its floors were first written from one sample of 6 and both were wrong. The confirming measurement therefore pools at least three samples. No floor is added: the check is already at 1.0, and a floor below it would legalise a fabricated draft.

**Assert the prompt, and assert that skill guidance does not displace it.** A fake model does not improvise a draft, so the behaviour is only measurable live — the same position `CreationStagingPromptTests` takes. Two things are statically checkable and both matter: the orchestrator's action block names the rule, and a run carrying activated skill guidance still carries the orchestrator's own instructions. The second is the one that would catch a regression in context assembly, which is where the competing text comes from.

## Risks / Trade-offs

- [The entry prompt fronts every turn, including retrieval, so a change here has the widest blast radius of any prompt edit] → both live gates are run after the change, not just the creation gate; a retrieval regression would show in `live-answer-hygiene.sh`.
- [The rule may still lose to the skill's numbered step, since that text arrives later in context] → accepted. If a pooled re-measure does not move the rate, the escalation is a skill-text change, which means revisiting the `does_not_steer_routing` ban and stating why the ban no longer holds. That is the next step, not this one, and it is recorded so the failure is not re-diagnosed from scratch.
- [The orchestrator could become over-eager to hand off, degrading simple turns] → the rule is scoped to steps requiring a tool it lacks, so a turn answerable from its own tools is unaffected; both live gates cover this.
- [A pooled measurement of ~18 turns is slow and costs provider calls] → necessary, and the alternative is reporting a 6-run pass as evidence, which is the error this repo has already made twice.

## Baseline (measured during apply)

The three pooled samples this change was specified against were not obtained. Eighteen fresh turns produced zero failures, and pooling the one earlier recorded failure gives 1 in 24 `stage` turns — roughly 1/24, not the 1/6 the change was written around.

That collapses the verification story, and it is worth being precise about why. At a 1/24 rate, a clean 18-turn sample is the *likely* outcome whether or not anything was fixed: the expected number of failures in 18 turns is 0.75, so roughly half of all unchanged runs would also come back clean. The measurement in task 4.1 therefore cannot distinguish "fixed" from "not fixed", and would report success either way.

The gate also cannot cheaply produce the sample this needs. One run is the four-probe sequence, and only one of those four is the `stage` probe, so a run yields exactly one `stage` sample. Distinguishing 1/24 from 0 at useful confidence needs on the order of 100 `stage` turns — about 100 runs, several hundred turns and hours of provider calls — for a prompt whose edit is a few sentences.

What survives is the static argument, which does not depend on sampling. The orchestrator holds no tool that stages a draft, and step 5 of the create skill is a six-line field template naming no tool, executable in prose from the user's own input. An agent can satisfy that step with nothing to fail, and one recorded turn did. The hole is real regardless of its rate; the rate only determines how often a user meets it.

The consequence for this change: it is a hardening justified by prompt structure, not a fix that can be shown to have moved a measured rate. The tasks are written to say so rather than to claim a verified improvement.
