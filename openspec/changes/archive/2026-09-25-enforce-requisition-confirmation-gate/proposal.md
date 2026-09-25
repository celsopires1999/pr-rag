## Why

The `create-purchase-requisition` skill under-activates on exactly the phrasings it exists to serve, and because the draft-then-confirm procedure lives only inside the skill body, its absence silently disables the confirmation gate: the model calls `create_requisition` directly and persists a requisition the user never confirmed.

Measured against a live `gpt-4o-mini` run of the current prompt (`## <AVAILABLE_SKILLS>` manifest and `activate_skill` registration both verified correct, so this is not a loading or wiring bug):

| User utterance | `activate_skill` called? |
| --- | --- |
| "I need to create a purchase requisition" | no |
| "Create a new purchase requisition for me" | no |
| "I want to draft a new purchase requisition" | no |
| "I want to CREATE a new purchase requisition, can you help?" | no |
| "please register a new item on a requisition" | yes |
| "open the create-purchase-requisition skill" | yes |

The skill activates for the awkward phrasings and not the obvious ones. Two prompt-level defects drive it:

1. The skill's frontmatter description is hedged ("Use this skill **ONLY** when ... **Do not use it for** questions about existing requisitions"), which sets a high activation bar.
2. `AgentInstructions.SkillsGuide` tells the model a skill "**NEVER adds, removes, or changes the tools available to you**", so activating one looks like a zero-value step. Meanwhile `create_requisition` is listed in `<ALLOWED_ACTIONS>` with all six parameters documented, so asking the user for the fields is itself a valid ReAct step and the model never spends a step on the skill.

The consequence is worse than a cosmetic routing miss. The confirmation gate has **no code-level enforcement anywhere** — verified: no draft/confirmed state exists in `PrRag.Application`; "confirm first" appears only as prompt text in `AgentInstructions.CoreInstructions` (twice) and in the `create_requisition` `[Description]`. Prompt text alone already failed to hold, which is why the observed run persisted a requisition with no confirmation turn. Relying on a skill body to carry a correctness guardrail means any routing miss becomes a data-integrity miss.

## What Changes

- **BREAKING** `create_requisition` refuses to persist unless the session holds a requisition draft that the user has explicitly confirmed. Unconfirmed calls return a structured refusal and persist nothing.
- Add a new `create_requisition_draft` tool that stages a draft in session state (field-level validation and the item-supplier combination check included) without persisting, and marks it `presented`.
- Add a new `confirm_requisition_draft` tool that flips a presented draft to `confirmed`, carrying a `yes`/`no` answer, and is the only path that unlocks persistence.
- Move the draft-then-confirm procedure out of the skill body and into the base prompt and the tool descriptions, so the gate holds even when the skill does not activate.
- Raise `activate_skill` from a suggestion to a requirement for create/draft/requisition-creation intent, and rewrite the skill description to state when to use it positively rather than as a narrow exception.
- Add a report field recording the confirmation path taken per turn, so gate compliance becomes observable.
- `create_requisition` reports and persists against a confirmed draft; direct field arguments become redundant and are validated against the draft rather than trusted independently.

## Capabilities

### New Capabilities
- `requisition-confirmation-gate`: The two-step draft/confirm state machine, the session state it owns, the refusal behavior of `create_requisition` when no confirmed draft exists, and its observability.

### Modified Capabilities
- `skill-framework`: Activation guidance changes from a soft suggestion to a requirement for requisition-creation intent, and the skill description is rewritten to be positively framed. The requirement that a skill "never changes the tools available" stays, but the guide no longer implies activating one is free of cost.
- `purchase-requisition-creation-guard`: The item-supplier combination guard gains a second, independent precondition (a confirmed draft), and `create_requisition` becomes a two-argument-draft flow rather than a single call with six free-form fields.
- `maf-agent-integration`: The agent exposes two additional tool functions and the report records the confirmation path.

## Impact

**Code**
- `backend/src/PrRag.Application/Services/Agents/PurchaseRequisitionTools.cs` — two new handlers, wire-name consts, registrations, and a rewritten `CreateRequisitionAsync` that reads the confirmed draft.
- `backend/src/PrRag.Application/Services/Agents/ToolResults.cs` — result DTOs for draft staging, confirmation, and the refusal.
- `backend/src/PrRag.Application/Services/Agents/SkillSessionState.cs` — currently owns only skill keys; gains the requisition-draft state bag keys, or a sibling helper if mixing the two concerns reads poorly.
- `backend/src/PrRag.Application/Services/Agents/AgentInstructions.cs` — `<ALLOWED_ACTIONS>` gains the two new tools; the draft-then-confirm procedure is added to `CoreInstructions`; `SkillsGuide` and the `activate_skill` bullet are reworded.
- `backend/src/PrRag.Application/Services/ChatService.cs` — the report gains the confirmation-path field.
- `data/skills/create-purchase-requisition.md` — frontmatter description reframed; body no longer the sole carrier of the procedure.

**Compatibility**
- The single-call `create_requisition` usage seen in the live walkthrough stops working by design; the model is instructed through the tool description and prompt to draft first. This is the intended break.
- `purchase-requisition-creation-guard` and `skill-framework` scenarios that assume the model can call `create_requisition` with six inline fields need their wording updated.

**Not affected**
- Database schema and migrations — drafts live in `AgentSession.StateBag`, not in Postgres.
- `create_requisition` persistence path, the item-supplier combination query, and the created-requisition listing.
- The observability report's existing fields.
- The DevContainer and compose topology.

**Verification note**
- The confirmation gate must be provable by a test that calls `create_requisition` with no confirmed draft and asserts nothing was persisted. The existing integration tests call `create_requisition` directly, so they are the ones that must be migrated to the two-step flow — this is the bulk of the work.
