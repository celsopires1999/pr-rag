## Context

`agent-workflow-handoff` extracted the read-only retrieval tools onto
`AgentIds.Retrieval` and left the write path on the orchestrator. The mechanism
that made the first extraction work is narrow, and this change is the first
capability to run into its limits:

- **One positional handoff per turn, and no return.** The graph is composed fresh
  each turn with the orchestrator as the entry point, `HandoffMessage` is
  positional, and `EnableReturnToPrevious()` is disabled because MAF would route
  back to the orchestrator unconditionally, where no participant can be
  re-activated.
- **Retrieval is single-turn; creation cannot be.** Retrieval answers one question
  in one turn, so a single handoff is sufficient. Creation is a two-phase gate:
  stage a draft, present it, get an explicit yes, write. The user's confirmation
  is a *new message*, so the exchange necessarily spans at least two turns. This
  is the first requirement in the graph that the one-handoff shape does not
  directly cover, and it is the reason this change needs a design rather than a
  repeat of the retrieval extraction.
- **Draft state is not reachable from the orchestrator.** Whether a draft is
  awaiting confirmation lives in `RequisitionDraftSessionState`, owned by the
  creation unit. The orchestrator holds no tool that reads it.
- **The observable failure is worse on the write path.** The retrieval extraction
  was verified with `RetrievalAttempted`, which separates *searched and found
  nothing* from *never searched*. The report has no equivalent for writes, so a
  turn that narrates a confirmation without calling `create_requisition` is not
  distinguishable from one that wrote a row by reading the report.

## Goals / Non-Goals

**Goals:**

- The orchestrator holds no tool that writes, and never holds a foreign unit's
  tool at all.
- The draft/confirm gate still holds, including the case where a user claims a
  confirmation that never happened in this session.
- Reaching the creation agent on a confirmation turn does not depend on the model
  choosing to hand off.
- The report distinguishes staged, confirmed, and absent write attempts, so the
  gate is assertable from the report and not only from the table.

**Non-Goals:**

- No change to gate semantics, the draft shape, the session endpoints, or the
  `purchase-requisition-creation-guard` / `requisition-confirmation-gate`
  requirements.
- No schema or migration.
- `AgentIds.SkillActivation` stays reserved. `activate_skill` remains on the
  orchestrator; extracting it is a separate question, because the orchestrator is
  the only agent given the skill manifest and so is the only one that can be told
  what exists.
- No attempt to let a single turn both fetch a catalog and write a requisition.

## Decisions

**The entry point becomes state-dependent: a turn with a draft awaiting
confirmation enters at the creation agent.** Chosen over having the orchestrator
hand off again on the strength of the replayed text.

The deciding factor is which failure each option produces when the model gets it
wrong. If the orchestrator must decide whether to re-route, the failure is that it
answers the user's "yes" itself — and, having the confirmation in front of it,
plausibly says the requisition was created with nothing written. The user
believes a row exists. Under composition-time routing the same wrong judgement
has nowhere to go: a staged draft stays staged, the user was just shown the
draft, and the next turn can still confirm it. A route that fails open on an
irreversible tool is not acceptable for this capability; failing closed is.

It is also deterministic. The routing decision stops depending on a model
inferring state it cannot observe, which is the class of defect this change exists
to remove — the shipped-skill text once told the orchestrator to call
`search_by_codes`, a tool it did not own, and the step silently never ran with no
test failing.

Rejected: *the orchestrator re-hands-off on the replayed text.* It cannot see
whether a draft is staged, so it would be guessing from wording, and its failure
mode is the open one above. Rejected: *enable return-to-previous.* It is disabled
for a recorded reason — return lands on the orchestrator, which cannot re-activate
a participant — so this would re-open settled work rather than build on it.

**Cross-capability lookups become a separate turn, carried by text history.** The
creation agent may not call the retrieval tools, and one turn admits one handoff,
so a turn cannot both look up codes and stage a draft. The resolution is already
how the system behaves: one turn resolves the codes through the retrieval agent,
and the user's next message carries them into the creation turn. This makes the
text-only history rule load-bearing rather than cosmetic — the conversation is the
transport by which a capability uses a value another capability fetched. It also
means a skill switch mid-confirmation is not supported, because the orchestrator
holding `activate_skill` is not on the path for a pending-draft turn.

**`create_requisition` stays the only authoritative validator.** The creation
agent's action block will not name retrieval tools, and combination validation
stays in the handler via `ExistsItemSupplierCombinationAsync`. The user therefore
learns an invalid pair at the gate rather than before staging. That is a worse
moment to learn it, and it is the right trade: the alternative reintroduces a
second capability's tools on an agent, and a check enforced in prose instead of in
code is exactly what the split already had to be repaired for.

**Add write-attempt tracking to the report, as the mirror of
`RetrievalAttempted`.** Three facts are needed and none are currently
distinguishable: a draft was staged, a confirmation was recorded, and a row was
written. A turn that claims a confirmation it did not perform is the write-path
analogue of the fabricated completion already fixed on the retrieval path, and
without this the creation gate can only be verified by querying the table after
the fact — which cannot tell "never attempted" from "attempted and refused".

## Risks / Trade-offs

- **The orchestrator is no longer an unconditional front door.** A pending-draft
  turn starts at the creation agent, so the user cannot switch to an unrelated
  request and expect the orchestrator to handle it in that same turn. Mitigation:
  the creation agent can hand off to retrieval, the draft stays staged and
  unconfirmed, and the user is not blocked. Fail-closed, but it is a real
  behaviour change and needs its own test rather than being assumed.
- **Skill activation is unavailable while a draft is pending.** Mitigation:
  state the limitation in the creation action block instead of letting the model
  discover it, and decide deliberately whether a skill switch should be allowed
  to abandon a staged draft.
- **The partition assertion changes shape.** Today it is expressed as "the
  orchestrator holds everything not yet extracted", which is a convenient way to
  make extraction incremental. Once only retrieval and creation are extracted, the
  orchestrator holds `activate_skill` alone, and the assertion should be written
  as full per-unit ownership with an explicit leftover set. Mitigation: update
  the test in the same change, and do not weaken it to accommodate the leftover.
- **Two live gates now depend on the same model.** The creation gate and the
  answer-hygiene gate both run the semantic and creation probes against a live
  API, and the creation agent now has a longer instruction surface. Mitigation:
  run both after the change; a regression in one is not evidence about the other.
- **The retrieval lookups in the creation prompt may go stale.** With the lookups
  unreachable from the creation agent, the action block must not name them. The
  shipped-skill tests already failed once on exactly this, so the guard should be
  extended to the creation unit rather than assumed.

## Migration Plan

No data, schema, or endpoint migration. The rollout is a code change behind the
existing composition: set `AgentSlug = AgentIds.Creation` and the composer
produces the agent. Rollback is reverting that one assignment, which returns the
tools to the orchestrator — the pre-change topology, which is known to work.

## Open Questions

- Should a user message on a pending-draft turn that is clearly unrelated abandon
  the staged draft, or leave it indefinitely? The state has no expiry today.
- Does the orchestrator need any awareness of a pending draft for the *first* turn
  of a new creation request, or is the creation agent's own first action to stage
  a draft sufficient to establish the pending state?
- Is `RequisitionDraftSessionState` the right place for the report's write-attempt
  facts, or does the report need its own record of them?
