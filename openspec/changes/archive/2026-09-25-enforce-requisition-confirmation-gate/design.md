## Context

`create_requisition` currently has one guardrail that blocks an unregistered item-supplier combination, and one that exists only as prose. The prose gate says "call only after the user explicitly confirms a drafted requisition" and appears in three places: `AgentInstructions.CoreInstructions` `<ALLOWED_ACTIONS>`, the `<STRICT_GUARDRAILS>` block, and the tool's own `[Description]`. None of it is code.

The live walkthrough that motivated this change (rebuilt `pr-rag-api` image, real key, `gpt-4o-mini`) persisted two requisitions from two sessions, neither preceded by a confirmation turn. Reproduced identically on `main` with the tool-contract change stashed, so it is not a regression from recent work.

The reason the prose gate fails is structural: the draft-then-confirm *procedure* is not in the prompt at all — it is in the body of the `create-purchase-requisition` skill, and the skill activates on 2 of 6 natural create phrasings. Routing miss, no procedure, no confirmation. Fixing activation alone would leave the correctness property resting on the same prompt text that already failed.

The constraint that shapes the whole design: the gate must hold even when the model ignores instructions. That means enforcement belongs in the tool, with the prompt reduced to steering.

## Goals / Non-Goals

**Goals:**
- Make "no confirmed draft, no requisition" a property the tool enforces, not a property the model is asked to respect.
- Keep the item-supplier combination guard as an independent second precondition.
- Make the gate observable: a report field records which path a turn took.
- Raise skill activation for requisition-creation intent as a UX improvement, on its own merits rather than as the carrier of correctness.
- Keep drafts ephemeral — session state, no schema change, no migration.

**Non-Goals:**
- Not a general tool-authoring framework or a permission system. One gate, for one tool.
- No new database table, column, or EF migration. A draft that is never confirmed is intentionally discarded with the session.
- Not changing retrieval, the semantic search threshold, the ingestion pipeline, or the report's existing fields.
- Not replacing the skill. The skill still improves the conversation; it just stops being load-bearing.
- Not a general-purpose multi-approval or audit-trail feature.

## Decisions

### 1. Enforce in the tool, not in the prompt

`create_requisition` returns a structured refusal and persists nothing when the session has no confirmed draft.

The alternative was prompt-only: reword `<ALLOWED_ACTIONS>`, make `activate_skill` mandatory, move the procedure into `CoreInstructions`. Cheaper, and it would probably raise activation from 2/6 to 5/6. It would still be a request. The evidence that prompt text is insufficient is the current bug — the gate is already stated three times in prose and the model skipped it anyway. A correctness property that depends on the model choosing a different step is not a guarantee.

This is the one place where the design deliberately spends more than the minimum. The prompt work in decision 4 is still done, but it is steering, not the mechanism.

### 2. Two new tools rather than one with a flag

`create_requisition_draft` stages and validates a draft in `AgentSession.StateBag`; `confirm_requisition_draft` marks a presented draft confirmed.

A single `create_requisition(..., confirmed: bool)` was rejected: `confirmed` is model-supplied, so it re-introduces exactly the failure mode being fixed — the model asserts the confirmation that the tool was supposed to verify. A separate `confirm_requisition_draft` tool makes the transition a distinct, observable act with its own argument (`answer`), and the report can then distinguish "model claimed confirmation" from "user actually answered".

Cost: two more tools in a five-tool set, and one more ReAct step per creation. Accepted — the tool count is not the constraint here.

### 3. Draft state lives in the session state bag, not Postgres

`RequisitionDraftSessionState` (a sibling to the existing `SkillSessionState`, same pattern: a static helper owning its own keys) holds `Draft`, `DraftPresented`, `DraftConfirmed`. Nothing is written to the database until the confirmed `create_requisition` call.

Rejected: a `drafts` table. A draft is single-session, single-use, and worthless once the session ends; a table would add a migration, a retention question, and a second source of truth for "what did the user agree to" — for data that never needs to outlive the conversation. The trade-off is that a draft cannot survive a session reset, which is the correct behavior for an unconfirmed proposal.

### 4. Fix activation as a UX fix, decoupled from correctness

`SkillsGuide` and the `activate_skill` bullet get reworded: activating a skill is worth a step because it produces a better guided conversation, not because it unlocks the gate. The skill's frontmatter description drops the "ONLY when ... Do not use it for" hedge for a positive statement of when to use it.

The `SkillsGuide` claim that a skill "NEVER adds, removes, or changes the tools available to you" stays — that is an accurate and useful property, and this change does not give skills new tools. What changes is the surrounding text, which currently reads as "activating one buys you nothing." With the gate no longer riding on activation, activation can be a genuine convenience rather than a correctness dependency, which is also why the skill body can keep its procedure text without that text being the sole copy anywhere.

### 5. The procedure is duplicated into the prompt on purpose

The draft-then-confirm steps are added to `CoreInstructions` and the `create_requisition` description while remaining in the skill body.

This is deliberate redundancy and reads as a violation of the single-source-of-truth preference that governs the tool *descriptions*. The distinction: the tool descriptions are the API contract consumed by `AIFunctionFactory`, where two sources of truth would be a maintenance bug. The prompt procedure is prose guidance, where duplication is a deliberate hedge against a routing miss — and the whole point of decision 1 is that correctness no longer depends on either copy. If one is edited out later, the gate holds; the user just gets a worse conversation. Recorded here so a future reader does not "fix" the duplication.

### 6. Direct field arguments are validated against the draft, not trusted

`create_requisition` keeps its six parameters and additionally requires a confirmed draft. The persisted values come from the draft. The arguments must match the draft, or the call is refused as inconsistent.

Keeping the parameters is deliberate: `FakeChatClient` scripts tool calls by argument name, and the report records arguments, so dropping them would make the observability report and the test harness lose the field values. Refusing on mismatch is what stops the model from drafting `ITM-...02` and then persisting `ITM-...08` on the confirmed call.

Alternative considered: `create_requisition` with no parameters at all, reading purely from the draft. Cleaner, and arguably more correct. Rejected for now because it makes the tool opaque in the report and forces a larger test migration; worth revisiting if the argument/draft drift proves annoying in practice.

## Risks / Trade-offs

- **[The model never learns to call the draft tool, so nothing is ever created]** → the failure mode is a dead end rather than a wrong write, which is the safe direction. Mitigated by putting both new tools in `<ALLOWED_ACTIONS>`, describing `create_requisition` as requiring a confirmed draft, and having the refusal message name `create_requisition_draft` and `confirm_requisition_draft` explicitly. The live loop in task 6 is what proves this.
- **[Argument/draft drift produces refusals the model cannot self-correct]** → the refusal message names the field that differs and restates the expected value, so the model can re-draft rather than retry blindly. A draft that has been confirmed is immutable; a mismatch means a fresh draft.
- **[Confirm then change your mind: the user answers yes, then asks to edit a field]** → the edited value is a new draft. Editing clears `DraftConfirmed`, and `create_requisition` refuses until the revised draft is presented and confirmed again. Requires the edit to route through `create_requisition_draft`, which the prompt requires.
- **[Two extra tools crowd the five-tool set and the model confuses them]** → both tools are documented as strictly ordered, `create_requisition`'s description opens by stating it requires a confirmed draft, and the refusal is explicit about the next step. If the live loop shows confusion between `create_requisition_draft` and `confirm_requisition_draft`, the fallback is merging the confirmation into the draft tool as an explicit `userConfirmed` field set only on a later turn — kept in reserve but not chosen, because it weakens the observability in decision 2.
- **[Test migration cost is high and touches unrelated-looking tests]** → every existing test that calls `create_requisition` directly must be migrated to the two-step flow. This is the bulk of the work and is called out in the proposal so it is not discovered halfway through.
- **[Prompt grows by roughly the procedure text]** → accepted; the alternative is relying on the skill body, which is the current bug.
- **[The gate blocks legitimate single-shot creation in tests and demos]** → deliberate. If a real workflow needs a pre-confirmed programmatic path, it should be a separate non-agent entry point, not a flag on the agent tool.

## Migration Plan

1. Land the state helper and the two new tools, with the gate still inert behind the existing single-call path.
2. Add the report field and the tests that assert the refusal when no confirmed draft exists.
3. Flip `create_requisition` to require a confirmed draft, migrating the existing tests in the same change.
4. Prompt and skill-description rewording, then the live loop.

Rollback is a revert of steps 3-4; steps 1-2 are additive and inert. No schema change, so no down-migration. Sessions already in flight at deploy time have no draft state and will simply be refused by `create_requisition` until the model drafts — the safe direction, and no user data is at risk.

## Open Questions

- Should the refusal be phrased as a recoverable error the model should act on, or a hard failure that surfaces to the user? Default is recoverable-and-actionable, on the assumption the model can self-correct within a session. Live behavior may contradict this.
- Should `confirm_requisition_draft` accept free-text ("yes, but make it 5") and reject it, or parse a quantity edit? Default is reject and ask for a re-draft; parsing natural-language edits is out of scope.
- Does the confirmation need to bind to a specific user turn for audit purposes? Nothing in the current report or schema captures who confirmed what, and the created-requisition row has no such column. Assumed not required; if audit becomes a requirement it needs its own change and a migration.
