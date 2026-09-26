## Why

The archived `modularize-agent-capabilities` change made each capability unit self-contained, but it deliberately stopped short of blast-radius reduction: after it, one agent still holds all seven tools and can still reach `create_requisition` on any turn. The remaining risk is a routing problem, not a code-organization problem, so it can only be fixed by making the capabilities separate agents. This change proves that on the read-only capability first.

## What Changes

- Add `Microsoft.Agents.AI.Workflows` 1.20.0, matching the pinned `Microsoft.Agents.AI` 1.20.0 exactly, so no version bump is forced.
- Split `SpecialistDefinition.Id` (stable slug) from `DisplayName` (human label), so a display rename cannot break telemetry correlation.
- Build the retrieval capability as a real MAF agent whose `Instructions` is its own `ActionBlock` and whose tool list is only its own three retrieval tools.
- Compose an orchestrator agent that keeps the four creation and skill-activation tools and gains a routing role, joined to the retrieval agent by a `HandoffWorkflowBuilder` graph exposed through `WorkflowHostingExtensions.AsAIAgent(...)`.
- Swap the `ChatClientAgent` inside `AgentRunService` for the workflow-backed `AIAgent`, leaving `IAgentRunService`, `ChatService`, the endpoints, SSE streaming, and the session store untouched.
- Introduce per-specialist session plumbing, with the retrieval agent using it immediately and the creation specialist adopting it in the follow-up change.
- Amend `skill-framework` so its "a skill never changes the tool set" invariant is scoped to what a *skill document* may assert, rather than claiming a single flat set of seven tools that no longer exists once capabilities are partitioned across agents.
- Record delegation visibility as `ILogger`-only, so the `RagQueryReport` schema stays frozen.

**This change is deliberately transitional and does not deliver write-path isolation.** An orchestrator with no tools at all would strand `create_requisition_draft`, `confirm_requisition_draft`, and `create_requisition` and break the guided creation flow, so the orchestrator retains them until the follow-up change. The blast-radius reduction this work is motivated by is therefore **read-path only at this stage**; `create_requisition` remains reachable from the orchestrator agent.

## Capabilities

### New Capabilities
- `agent-workflow-handoff`: multi-agent routing over an MAF workflow graph, stable agent identity slugs, per-specialist sessions, and the orchestrator/specialist tool partition.

### Modified Capabilities
- `specialist-capabilities`: a capability unit becomes an addressable agent rather than a tool group; the single-catalog binding point becomes workflow participant binding; the partition guarantee is restated as a per-agent tool assignment.
- `maf-agent-integration`: agent registration and tool registration move from one `ChatClientAgent` to a workflow-backed `AIAgent` presented through the same seam.
- `agent-framework-layering`: the composition layer composes a workflow graph instead of a single agent, while `ChatService` stays a thin adapter.
- `skill-framework`: the "skills guide without granting new capabilities" invariant is rescoped from "the flat seven-tool set" to "the fixed union across specialists, with routing decided by the orchestrator".
- `continuous-conversation`: session state is scoped per specialist, so a specialist's turn context is not the orchestrator's.

## Impact

- **Code:** `DependencyInjection`, `AgentRunService`, `AgentInstructions`, and the `Services/Agents/Specialists/` namespace gain a workflow composition path. `IAgentRunService` is unchanged by design.
- **Dependencies:** one new NuGet package, `Microsoft.Agents.AI.Workflows` 1.20.0, in `PrRag.Application` and `PrRag.Infrastructure` to match the existing MAF pin.
- **Specs:** one new capability spec, five modified. `RagQueryReport` and the front-end are untouched.
- **Data:** no database schema change, no EF migration, no API contract change.
- **Risk:** handoff adds model routing decisions, and the archived Phase 1 live findings record this model losing a staged draft across turns and hallucinating a draft. The change is therefore gated on a live walkthrough rather than a green suite, and a live regression blocks the follow-up change instead of shipping.
