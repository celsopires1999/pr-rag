## Context

The archived `modularize-agent-capabilities` change left the agent as a single `ChatClientAgent` holding all seven tools, composed in `AgentRunService` from `ISpecialistCatalog.AllTools` and `AgentInstructions.ComposeSystemPrompt`. Three capability units exist (`RequisitionSearchSpecialist`, `RequisitionCreationSpecialist`, `SkillActivationSpecialist`), each pairing its `ActionBlock` prose with its own tool list, but those units are a *composition seam*, not *agents*.

`IAgentRunService` is the abstraction that keeps the rest of the application ignorant of agent construction: `CreateSessionAsync`, `RunAsync`, `RunStreamingAsync`, all typed against `AgentSession`/`AgentResponse`/`AgentResponseUpdate`. D8 in the archived design recorded that adopting a workflow graph would not re-plunge the application, because `WorkflowHostingExtensions.AsAIAgent(...)` returns an `AIAgent`.

Verified against the `Microsoft.Agents.AI.Workflows` 1.20.0 package before this design was written, rather than inherited on trust:

- The package publishes exactly one 1.20.x version, so it matches the pinned `Microsoft.Agents.AI` 1.20.0 with no forced bump.
- `WorkflowHostingExtensions.AsAIAgent(Workflow, string, string, string, IWorkflowExecutionEnvironment, bool, bool)` exists, taking the workflow plus id, name, description, an execution environment, and two flags.
- `HandoffWorkflowBuilder(AIAgent)`, `AddParticipants(IEnumerable<AIAgent>)`, `WithHandoff(AIAgent, AIAgent, string)`, `WithHandoffs(AIAgent, IEnumerable<AIAgent>)`, `WithHandoffInstructions(string)`, `EnableReturnToPrevious()`, and `Build()` all exist.
- Probing a composed graph revealed two things the XML docs do not state. The handoff tool the orchestrator is offered is generated positionally as `handoff_to_1` and cannot be named, and `EnableReturnToPrevious` means "route subsequent user turns straight back to the specialist that handled the last turn" — sticky routing that bypasses the orchestrator — not "return control to the orchestrator". Both are recorded as constraints in D3 and D4 rather than assumed.
- `EmitAgentResponseEvents(bool)` and `EmitAgentResponseUpdateEvents(bool)` exist on the builder, which is the streaming seam the SSE endpoint depends on.

## Goals / Non-Goals

**Goals:**

- Make the handoff seam real and proven on the read-only capability, so the follow-up change can extend it to the two write-path specialists with the topology already trusted.
- Isolate retrieval so the read-only tools are no longer bound to the agent that can also reach `create_requisition`.
- Give agents stable machine identities distinct from their display names, so a rename cannot break telemetry.
- Keep `IAgentRunService`, `ChatService`, the endpoints, SSE streaming, and the `RagQueryReport` schema unchanged. The session store's *interface* does change — see D9; it is no longer a session cache.
- Rescope the `skill-framework` invariant so the spec describes the new reality instead of a flat seven-tool set that no longer exists.

**Non-Goals:**

- Write-path isolation. `create_requisition` stays reachable from the orchestrator in this change.
- Durable sessions. `WithCheckpointing` is available and would fix in-memory session loss, but it is orthogonal to routing and would widen the blast radius of a change that already carries model risk.
- Migrating the skill framework to MAF's `AgentSkillsProvider`. Same reason, plus it touches the same files.
- Revising the orchestrator's `AgentDescription` beyond what routing requires.

## Decisions

### D1: Handoff, not tool-style delegation

`AsAIFunction`-style delegation returns the specialist's text to the orchestrator as a function result, which the orchestrator must then paraphrase to the user. For a flow where the specialist must *ask the user* for the next missing field, that is a paraphrase hop on every turn, and each hop can distort a field the user just typed. Handoff transfers the conversation, so the specialist talks to the user directly and the paraphrase disappears.

`RoutePersistingRoutingChatClient` is not an alternative: it swaps the chat client, i.e. the model, not the agent's tools or identity, so it cannot change which agent is answering.

### D2: The topology is transitional, and the proposal says so

Only retrieval is extracted here, so the orchestrator must retain `create_requisition_draft`, `confirm_requisition_draft`, `create_requisition`, and `activate_skill`. An orchestrator with no tools would strand the guided creation flow entirely. The result is a hybrid agent: it both answers and routes, but only for the capabilities that have not yet been split out.

This is the least-risk way to prove the topology, and it is explicitly recorded as partial. The blast-radius reduction motivating the work is read-path only until the follow-up change.

### D3: `Id` and `Name` split, seeded from the existing definition

MAF documents `AIAgent.Id` as being for "tracking, telemetry, and distinguishing between different agent instances in multi-agent scenarios". `SpecialistDefinition.Id` already carries stable slugs (`purchase-requisition-retrieval-specialist`, and the orchestrator's `purchase-requisition-orchestrator`); these are the seeds. `DisplayName` becomes purely the human label.

The slugs are renamed to a dotted `prrag.*` form at agent composition, because a workflow's participant identity is what handoff resolution matches on and a long prose slug is not a good identity. A display rename must therefore never touch the slug, which is the whole reason the two are separate properties.

**Verified limit on this decision:** the slug is stable, but the *handoff tool the orchestrator is offered* is not. Probing a composed graph showed the orchestrator is offered `handoff_to_1`, generated positionally by the library. `WithHandoff(from, to, handoffReason)` takes only a reason — there is no overload that names the generated tool. So the slug gives stable agent and telemetry identity, while the tool name the model actually sees is opaque and will shift if participants are added or reordered. Routing is therefore identifiable in logs by agent slug, not by the name the model called, and nothing in the observability report may be built on the handoff tool name. This is a library constraint, accepted rather than worked around: naming it would mean forgoing the library's handoff machinery.

### D4: Every turn enters at the orchestrator, so return-to-previous stays off

`EnableReturnToPrevious` is deliberately not enabled. Its name reads like "give control back to the coordinator", but its actual behaviour is to send subsequent user turns directly to whichever specialist handled the previous turn, skipping the orchestrator entirely.

That is the wrong shape here for two reasons. Skill guidance is injected per turn by the turn orchestrator, and the per-specialist session scoping in D7 is only observable if turns are entered uniformly; sticky routing would make both depend on which agent happened to run last. Leaving it off keeps the orchestrator as the single entry point, so a turn always has the orchestrator's context and the injected guidance available, and routing is re-decided per turn instead of inherited from the previous one.

### D5: Each specialist's `Instructions` is its own `ActionBlock`

`AgentInstructions.ComposeSystemPrompt` currently takes the skill manifest plus every unit's action block. In the split, the orchestrator composes `CoreInstructions` plus the action blocks of the units it still owns, and a specialist composes only its own `ActionBlock`. This is why Phase 1 put the prose in the same object as the tools: nothing has to be re-derived by hand here.

A specialist therefore cannot see another capability's bullets, which is the least-privilege property being bought — an agent cannot be told to use a tool it does not hold.

### D6: The workflow is exposed as an `AIAgent`, so the seam holds

The decisive property is that `AsAIAgent(Workflow, ...)` returns an `AIAgent`. `AgentRunService` swaps its `ChatClientAgent` field for that `AIAgent` and its `ChatClientAgentRunOptions` for the workflow's own run options. `IAgentRunService`'s three methods keep the same signatures and the same return types, so `ChatService`, the HTTP endpoints, and the SSE streaming path are untouched by construction.

### D7: Streaming is wired explicitly and guarded by a test

`EmitAgentResponseEvents` and `EmitAgentResponseUpdateEvents` control whether the workflow surfaces agent responses as events or as update events. SSE streaming depends on the update path. This is the most likely part of the change to regress silently — a workflow that returns no updates would still pass every non-streaming test and would break the front-end at runtime — so it gets a dedicated test rather than relying on the existing suite.

### D9: The session store owns the conversation, not a cached session

Found while implementing, not designed up front, and it is the one decision here
that reverses a Phase 1 assumption.

MAF binds an `AgentSession` to the agent instance that created it. Phase 1 cached
one session per client session id, so every turn after the first ran against the
*first* turn's agent. That was invisible while there was one agent per request,
because `ChatService` composed the prompt per request from the live manifest. It
stops being invisible the moment the agent graph exists: the cached session pins
one composition of the graph, and that graph captures request-scoped services
(`IPurchaseRequisitionRepository`, `AgentTurnContext`) in its tool delegates. So
turn 2 ran a disposed scope's tools, and the skill manifest stayed frozen at
whatever turn 1 saw — which is exactly the regression
`SystemPromptChannelTests.A_manifest_reload_reaches_an_in_flight_session_on_its_next_turn`
exists to catch. Proven by tagging each composition with a nonce and observing
that both turns carried the same one.

A process-wide graph is not the fix, for the same scoped-services reason. So the
direction is the other way: the session becomes per-turn, and what must outlive
a turn moves out of the session. `IAgentSessionStore` now hands out a fresh
`AgentSession` per turn and holds an `AgentSessionState` per client session id
carrying the conversation, the active skill, and the staged draft — the last two
having also lived in `AgentSession.StateBag`. Each agent is given an inert
`ChatHistoryProvider`, because otherwise the agents would keep a second,
per-turn-scoped copy of a history the application already owns. `ChatService`
replays the stored history into the run and records the turn as the question and
the answer the caller was shown. See D12 for why the run's own messages are not
the source of that answer, and why the recorded conversation carries no tool
traffic.

What this costs: MAF's between-turn workflow state is gone, so routing is
re-decided from scratch every turn. That is the behaviour this graph wants anyway
— see D7 — and it is why the loss is not a regression.

The draft living in the state bag is worth a note. It is a plausible contributor
to the cross-turn draft loss the Phase 1 live gate recorded, and it is now
readable state on a record the tests can construct directly.

### D8: Per-specialist sessions, introduced now and adopted next

A specialist's turn context is not the orchestrator's. The Phase 1 live findings recorded the model losing a staged draft across turns and, in one run, hallucinating a draft. Per-specialist sessions are the plausible mechanism by which the creation specialist would hold a draft in its own context instead of relying on the orchestrator to carry it, so the plumbing is introduced here and the hypothesis is validated in the follow-up change. Stating this as a hypothesis rather than a fix is deliberate: it is unverified, and the retrieval agent in this change gives it no direct evidence either way.

### D10: The `skill-framework` amendment is a deliberate edit, not drift

The current requirement says a skill "SHALL NOT add, remove, or change the tool functions available to the chat model", and its scenario enumerates "the fixed framework set" of seven names. Once capabilities are partitioned across agents, that enumeration is no longer the shape of the system: the orchestrator no longer holds all seven, and the union is no longer something any single agent can see.

The invariant is rescoped to what a *skill document* may assert: a skill describes a procedure and cannot grant or remove capability, while which agent holds a tool is the orchestrator's routing decision. The scenarios are restated, not deleted, and the guardrail property the requirement exists to protect — a skill cannot become a precondition for a tool to be callable — is preserved verbatim.

### D11: Delegation visibility stays out of the report

Routing decisions are observable through `ILogger` only. `RagQueryReport` keeps its exact schema, so report consumers and the observability report's own spec are unaffected. The archived design reached the same conclusion; this change keeps it.

## Risks / Trade-offs

- **Handoff adds model routing decisions to a model already struggling** → the Phase 1 live record shows a lost draft across turns, one hallucinated draft, and an earlier archived finding that `activate_skill` was not called in 4 of 6 live runs. Adding routing on top of that is the central risk. Mitigation: retrieval is read-only, so a routing failure degrades an answer rather than a write; the change is gated on a live walkthrough; a live regression blocks the follow-up change instead of shipping.
- **A hybrid orchestrator is not the end state and could calcify** → the orchestrator both answers and routes, which is the topology Phase 1 already had, plus routing. Mitigation: recorded in the proposal and here as transitional, and the follow-up change's first task is to empty the orchestrator's tool list.
- **Streaming could break without any test failing** → D6.
- **A shared DI lifetime could reintroduce the single tool list** → the risk that all participants receive the catalog's combined list, which is the exact defect caught in Phase 1 when the catalog produced 21 tools. Mitigation: the partition assertion is restated as a per-agent assignment check, so a specialist that holds another capability's tools fails a test.
- **The `skill-framework` amendment could quietly widen what a skill may do** → the risk in rescoping an invariant is that the rescoped version permits something the original forbade. Mitigation: the guardrail scenario about activation not being a precondition for tool availability is carried across unchanged, and the amendment is written as a delta with scenarios restated rather than as prose drift.
- **A live regression may be misattributed to the model** → the Phase 1 work already showed this model fails a multi-turn flow that the pre-change code also failed, which is why the baseline comparison was run then. Mitigation: if the live gate fails, compare against the Phase 1 commit before concluding the topology is at fault.

### D12: A run's messages are not the answer, and history carries no tool traffic

Found by the live gate, after the unit suite was already green, and it is the
reason the recorded conversation is text-only.

`includeWorkflowOutputsInResponse` is `false`. Left `true`, the workflow folds
the whole run back as outputs, and the request is the first thing in it, so
`response.Text` and the streamed deltas arrive as the conversation's own history
in front of the new answer. Turn 2 answered with turn 1's question, turn 1's
answer, then turn 2's answer. The graph was never wrong here; the *reporting* of
the run was.

That is also why `RecordTurn` takes the run's text and ignores
`response.Messages`. Reassembling the conversation out of those messages is the
one move that brings the echo back.

Setting the flag to `false` was not sufficient on its own: with it false the
run's messages no longer carry the final assistant text either, so recording
from them left the history with an empty answer. The answer therefore comes from
the run itself, and the recorded assistant turn is by construction the text the
caller was shown. `Answer_is_the_new_reply_and_not_the_replayed_transcript` pins
this and fails if the messages are concatenated back into the answer.

The other half is that history holds no tool traffic, which was found as a
`HTTP 400 invalid_request_error: messages with role 'tool' must be a response to
a preceeding message with 'tool_calls'` on every turn after the first. Neither
half of a call/result pair is replayable. Across a handoff the specialist makes
the call, so the workflow surfaces the result with no call of its own; and the
framework batches results into a single message whose last call is emitted as a
*later* assistant message, so no prefix of a tool transcript is well-formed. The
failure is not recoverable by ordering or by dropping orphans, and it fails the
whole turn rather than degrading the replay. The answer already carries the
grounded rows, so a follow-up is answerable from text.

The cost is real and deliberate: a follow-up can no longer be answered from the
raw rows a tool returned, only from the rows the previous answer quoted. For a
follow-up that needs a *different* slice of a large result set, that is a
regression against replaying tool results, and it is the thing to revisit first
if follow-up grounding proves thin in use.

Rejected on the way: filtering the streamed text against the messages that were
sent. It looks safe and is not — a new answer can be byte-identical to an earlier
one, and the filter eats the real output instead of the echo. The unit suite
caught it, which is the argument for keeping the fake's scripted answers
degenerate.

