## Why

The orchestrator is the only agent that can write to the database. It is also the
agent whose job is to answer directly and route to others, and it shares
`activate_skill` with the write path. Requisition creation is the one capability
where a wrong tool call persists a row, and the capability that guards against
that — the two-phase draft/confirm gate — currently shares an agent with answer
routing rather than owning one. Extracting the retrieval tools in
`agent-workflow-handoff` proved the per-unit binding works; the write path is the
remaining case, and leaving it on the orchestrator means the agent least
constrained by a single job is the one holding the irreversible tools.

## What Changes

- Bind `RequisitionCreationSpecialist` to its own agent, `AgentIds.Creation`.
- Remove `create_requisition_draft`, `confirm_requisition_draft`, and
  `create_requisition` from the orchestrator. After this change the orchestrator
  holds only `activate_skill` and answers for what it can still do.
- Route requisition-creation requests to the creation agent by handoff, in the
  same positional single-handoff shape the retrieval extraction already uses.
- Do not give the creation agent the retrieval tools. The partition invariant
  holds that an agent may only call tools its own action block describes, so the
  lookups stay with the retrieval unit and the authoritative combination check
  stays inside `create_requisition`, which already refuses invalid pairs.
- Keep the draft/confirm gate behaviourally identical. No requirement in
  `purchase-requisition-creation-guard` or `requisition-confirmation-gate`
  changes; only the agent that holds the tools does.

## Capabilities

### New Capabilities

None. This change completes a boundary that `specialist-capabilities` already
defines, rather than introducing behaviour the system does not have.

### Modified Capabilities
- `specialist-capabilities`: "Capability tool partition is verifiable" currently
  asserts that the units partition the tool set and that anything not yet
  extracted stays on the orchestrator. After this change every unit is owned by
  exactly one agent and the orchestrator holds no tool that writes, so the
  requirement gains per-agent ownership and an explicit leftover allowance.
- `rag-observability-report`: the report records the tool call list and the
  retrieved requisitions, but nothing distinguishes a *refused* write from a
  *performed* one, or a staged draft from a written row. Adding write-attempt
  facts is what makes the moved gate assertable from the report instead of only
  from the table.

## Impact

- `Specialists/RequisitionCreationSpecialist.cs` — set `AgentSlug`;
  `AgentGraphComposer` picks up the agent with no signature change.
- `Services/Agents/AgentIds.cs` — `AgentIds.Creation` stops being reserved and
  becomes bound, which also retires the "not yet bound" note on its doc comment.
- The orchestrator's `ActionBlock` loses its write bullets and gains a handoff
  trigger, so the `SpecialistCatalog` partition assertion changes: retrieval,
  creation, and skill-activation are each fully owned by one agent, with the
  orchestrator owning only what is left over.
- `AgentFrameworkLayeringTests` — the per-unit partition and the reserved-slug
  assertions must be updated together; the current reservation invariant is
  written in terms of "not yet bound".
- No API, schema, or endpoint change. No migration.
- Live verification is needed, and its tool does not exist yet. The write path's
  confirmation gate is not testable in process — a fake model does not enforce a
  confirmation, so the requirement that the gate holds cannot be asserted the way
  the routing requirements are. The four cases this change must not regress are
  known (see `tasks.md` 1.2 and 5.2), but the probe that checks them is currently
  an ad-hoc script run by hand, not a file in the repository. Writing
  `scripts/live-creation-gate.sh` is therefore a prerequisite task in its own
  right, not a run of something already present.
