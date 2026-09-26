## Context

`AgentRunService` is the only place a MAF agent is constructed: `chatClient.AsAIAgent(name, description)` with `instructions` left null, and the seven tools bound through `ChatClientAgentRunOptions → ChatOptions.Tools` in the constructor. Because the service is registered `AddScoped`, the composed agent — and the tool handlers it captures — are rebuilt per HTTP request, which is what lets the handlers hold a request-scoped `AgentTurnContext`.

Everything the model sees is currently described in three places that do not know about each other:

1. `PurchaseRequisitionTools` — seven `[Description]`-annotated handlers plus the seven wire-name constants plus the `RegisterFunction`/`RecordToolCall`/`LogResult` boilerplate. One class, 469 lines.
2. `AgentInstructions.CoreInstructions` — a 42-line `const` string whose `<ALLOWED_ACTIONS>` bullets are the model's only narrative documentation of those seven tools. It is cross-referenced from the shipped skill markdown at `data/skills/create-purchase-requisition.md`.
3. `AgentInstructions.ComposeSystemPrompt` — assembles the core text with the skill manifest.

The consequence is that the prompt's tool documentation and the tool list are connected only by a `const` string and a test that asserts one is a substring of the other (`AgentFrameworkLayeringTests.cs:49`). Nothing makes a tool's bullet and its handler travel together.

Two further constraints shape the refactor:

- The `agent-framework-layering` spec requires tool metadata to be declared exactly once and forbids forwarding lambdas, because `AIFunctionFactory` derives the JSON schema from the registered delegate's `MethodInfo` and a lambda silently drops every parameter `[Description]`. A refactor that reorganizes registration is therefore one careless lambda away from reinstating a bug that was already found and fixed once.
- The `purchase-requisition-creation-guard` and `requisition-confirmation-gate` capabilities put the write path behind a gate enforced in code (`CreateRequisitionAsync`), explicitly *not* in prompt text, because advisory instructions proved unreliable. The guard must not be weakened or relocated.

`microsoft.agents.ai` 1.20.0 and `microsoft.agents.ai.abstractions` 1.20.0 are the only agent packages referenced. Verified present in 1.20.0 and available without a version bump: `AIAgentExtensions.AsAIFunction(agent, options, session)`, `ChatClientAgent.Instructions` (settable), `AIAgentBuilder` and its `Use` pipeline, `AgentSessionRoutingState`. The project is layered Api → Infrastructure → Application; `PrRag.Application` holds the agent composition and takes no Infrastructure dependency.

## Goals / Non-Goals

**Goals:**

- Make each capability — retrieval, creation, skill activation — a self-contained unit that owns its handlers, its registrations, and the prompt text documenting them, so the three cannot drift.
- Introduce a single seam that binds capabilities to the agent, shaped so Phase 2 can turn each capability into an agent without rewriting tool or prompt content.
- Close a live spec drift: the compiled prompt is currently composed once per session, so a `SkillsWatcherService` reload never reaches in-flight sessions, contradicting `skill-framework/spec.md:27-29`.
- Fix a prompt self-contradiction: the "always check for a matching skill" rule is emitted even when the manifest is empty.
- Preserve every model-facing and data-facing contract: wire names, tool and parameter descriptions, result shapes and message strings, guardrail behavior, `RagQueryReport` schema, chat response body, API surface.
- Pin the Phase 2 direction in a decision so the seam is built for a known target rather than an imagined one.

**Non-Goals:**

- No runtime multi-agent behavior. One `ChatClientAgent`, bound to every capability, exactly as today.
- No new tool, no removed tool, no renamed wire name, no reworded `[Description]`, no changed `ToolResults` shape or message string.
- No change to the confirmation gate's logic, location, or the fact that it lives in code rather than in prompt text.
- No change to the `RagQueryReport` schema. Delegation visibility in Phase 2 is `ILogger`-only, per decision.
- No new NuGet package in this change. `Microsoft.Agents.AI.Workflows` is a Phase 2 dependency.
- No replacement of the bespoke skill framework with MAF's `AgentSkillsProvider`. That exists in 1.20.0 and is a genuine candidate, but it is a separate change.
- No change to the API, database, ingestion, or front end.

## Decisions

### D1: A capability is a `SpecialistDefinition` pairing tools with the prompt that documents them

```csharp
public sealed record SpecialistDefinition(
    string Id,
    string DisplayName,
    string ActionBlock,
    IList<AITool> Tools);
```

The `ActionBlock` is the `<ALLOWED_ACTIONS>` fragment covering exactly the tools in `Tools`, held by the same class that holds the handlers. This pairing is the point of the whole change: it is the only structural link that keeps a tool's documentation attached to its implementation, and it is what makes Phase 2 a change in *how capabilities are bound* rather than a rewrite of content.

Rationale: Phase 2 turns each definition into one `ChatClientAgent` whose `Instructions` is that definition's `ActionBlock` and whose tool list is that definition's `Tools`. If the prompt text and the tool list had stayed in separate files, Phase 2 would have needed to re-derive one from the other by hand.

Alternative considered: keep one `AgentInstructions` with all bullets and have Phase 2 split it by string manipulation — rejected; it re-creates the drift risk the change exists to remove, at exactly the moment drift is most expensive.

Naming note: the ids are `purchase-requisition-orchestrator` (renamed from `purchase-requisition-agent`), `purchase-requisition-retrieval-specialist`, and `purchase-requisition-creation-specialist`. "Specialist" is forward-looking — in Phase 1 a specialist is a capability unit, not an agent.

### D2: Wire names move to a shared `ToolNames` type

All seven constants move out of `PurchaseRequisitionTools` into one static class. They are consumed by registration, by `RecordToolCall`, by the shipped skill markdown's expectations, by `AGENTS.md`, and by tests in three files.

Rationale: the `agent-framework-layering` requirement is that a wire name is declared once and shared by registration and report bookkeeping. Leaving the constants on a class that is being dissolved would either break that requirement or tempt per-specialist duplicates, which is precisely the drift the requirement forbids.

Alternative considered: a `const` per specialist class — rejected, it makes "declared once" unenforceable from the outside, since nothing would prevent two capabilities from claiming the same name.

### D3: Shared boilerplate via a composed `SpecialistToolSet`, not a base class

`SpecialistToolSet` holds the `List<AITool>`, the scoped `AgentTurnContext`, and the `ILogger`, and exposes `Add(wireName, handler)`, `Record(name, arguments)`, and `Log(name, argNames, count, elapsed)`. Each specialist composes one in its constructor and registers its annotated method groups through it.

`Add` preserves the exact `AIFunctionFactory.Create(handler, new AIFunctionFactoryOptions { Name = wireName })` call with **no** `Description` override, because the handler's `[Description]` is the single source.

Rationale: composition over inheritance matches the existing style — `AgentTurnContext` is injected into every tool class rather than reached through a base. It also keeps the annotated-method-group rule visible at each call site instead of hidden behind a virtual method.

Alternative considered: an abstract `AgentToolSetBase` — rejected; it puts `[Description]`-carrying handlers one inheritance level away from their registration, which is how the lambda defect originally appeared.

`Record` and `Log` move onto the shared helper unchanged. `Record` is report bookkeeping, not tracing, and it must keep writing to the same scoped `AgentTurnContext` so `RagQueryReport.ToolCalls` stays byte-identical.

### D4: `CoreInstructions` keeps only cross-cutting text

Retained: agent identity, the ReAct loop, "do not output reasoning to the user", `<DATA_DICTIONARY>`, and the universal `<STRICT_GUARDRAILS>` (zero hallucination, language match, one requisition per session, historical data blindspots). Moved out: the per-tool `<ALLOWED_ACTIONS>` bullets and the creation-gate rules, which land in the owning specialist's `ActionBlock`.

`ComposeSystemPrompt(manifest, actionBlocks)` composes core text + the concatenated action blocks + the skills section. `AgentInstructions` stays the prompt composer, so `maf-agent-integration`'s requirement that compiled instructions come from a dedicated type still holds.

Rationale: `AgentFrameworkLayeringTests.cs:49` asserts `Assert.Contains(AgentInstructions.CoreInstructions, prompt)`, which survives a shrunken `CoreInstructions` because it is a containment check. The cross-cutting rules stay in one place so the guardrails cannot be duplicated per capability and drift apart.

The creation-gate rules move to the creation `ActionBlock` next to the code that enforces them — which is an improvement, since prompt text describing a gate now sits beside the gate.

### D5: The skill-first rule moves into the skill block, fixing the empty-manifest contradiction

`CoreInstructions` currently ends with "IMPORTANT: always check if there is a matching skill before taking any other action…", emitted unconditionally. When no skills are loaded, `ComposeSystemPrompt` appends "No skills are available." to the very same prompt. The prompt therefore instructs the model to consult an empty catalog.

Moving the rule next to `SkillsGuide` — which is already omitted in the empty-manifest branch — resolves the contradiction and puts the routing instruction where the manifest it refers to is rendered.

Rationale: the repo's own comment at `AgentInstructions.cs:34-35` states the intent, "with no skills loaded the guide is omitted rather than left to contradict", and the implementation just missed a second copy of the same advice.

### D6: Phase 1 binds the whole catalog into one agent

`AgentRunService` resolves `ISpecialistCatalog`, concatenates every `ActionBlock` into one prompt, and concatenates every `Tools` list into one `ChatOptions.Tools`. The model sees exactly the seven tools and effectively the same prompt it sees today.

Rationale: this is what makes the refactor verifiable. `ToolSchemaTests` and `AgentFrameworkLayeringTests` keep asserting the same seven wire names, the same descriptions, and the same schemas, and `SkillFrameworkTests` keeps scripting the same tools in the same order against the same `AgentTurnContext`. A green suite is therefore evidence of behavior preservation rather than of tests rewritten to match new code.

### D7: The system prompt moves to `ChatClientAgent.Instructions`

`AgentRunService` takes `ISkillService` and calls `AsAIAgent(name, description, instructions: AgentInstructions.ComposeSystemPrompt(...))`. `ChatService.BuildTurnMessages` stops emitting the `ChatRole.System` message and keeps only the active-skill body injection and the user turn; its `created` parameter becomes unused and is removed.

`instructions` is an **optional** parameter on the overload already in use, so this is a one-argument change. `AgentRunService` is `AddScoped`, so the agent — and therefore the prompt — is rebuilt per request, which is what makes the manifest fresh.

Two consequences worth stating plainly:

- **This fixes a real spec violation.** `skill-framework/spec.md:27-29` requires that a reloaded skill manifest reach subsequent requests. Today the prompt is composed once per session, so `SkillsWatcherService`'s 5-second-debounced reload never affects a live session. The fix makes the spec true.
- **It is not free.** Whether MAF's `ChatHistoryProvider` also persists the injected instruction into accumulated history is **not determinable from the package's XML documentation**. If it does, each turn adds a copy of the prompt, and the prompt is ~2.5k tokens — an invisible cost that compounds per turn and never fails a test. The mitigation is a guard test written *before* the migration: turn 2 of a session must contain exactly one manifest section. If the test fails, the fix is to keep prompt assembly in `ChatService` and instead recompose it per turn rather than only on `created`, which achieves the same spec compliance with no duplication risk.

Cost when it works: one additional prompt copy per turn. Not a doubling — full history is already resent on every call, so the copy was being paid for regardless; the change buys freshness rather than adding a new cost category.

### D8: Phase 2 is a handoff workflow, and `IAgentRunService` does not change

Recorded so the seam is built for a known target.

Phase 2 adopts `Microsoft.Agents.AI.Workflows` (1.20.0 exists, so no version bump past the pinned `Microsoft.Agents.AI` 1.20.0 is forced) and builds a `HandoffWorkflowBuilder` graph over three agents. The decisive fact, verified in that package: `WorkflowHostingExtensions.AsAIAgent(Workflow, id, name, description, executionEnvironment, includeExceptionDetails, includeWorkflowOutputsInResponse)` returns an `AIAgent`. So `AgentRunService` swaps its `ChatClientAgent` for a workflow-backed `AIAgent` and **`IAgentRunService`, `ChatService`, the endpoints, SSE streaming, and the session store are all untouched.** Adopting a graph does not re-plumb the application.

Handoff is the chosen topology over tool-style delegation (`AsAIFunction`) for one concrete reason. In the guided creation flow the specialist must *ask the user* for the next missing field. With `AsAIFunction` the specialist's text comes back as a `FunctionResultContent` and the orchestrator has to paraphrase it to the user, every turn, forever: for the 12-step flow in `data/skills/create-purchase-requisition.md` that is roughly 24 model calls and 12 paraphrase hops, and each paraphrase is a chance to distort a field the user just typed. Handoff removes the paraphrase entirely. `RoutePersistingRoutingChatClient` is not an alternative — verified, it swaps the *chat client* (the model), not the agent's tools or instructions, so it cannot change agent identity.

**In Phase 2, `Id` and `Name` must split.** MAF documents `AIAgent.Id` as being for "tracking, telemetry, and distinguishing between different agent instances in multi-agent scenarios". Phase 1's single agent has a randomly-generated id and it does not matter. A workflow holding three agents needs a stable slug id (`prrag.orchestrator`) distinct from the display name, so a later display rename does not break telemetry correlation. `SpecialistDefinition.Id` is the seed for that slug.

Phase 2 also brings: the skill-markdown rewrite and the `skill-framework` spec change; per-specialist sessions; and `AgenticRetrievalTests.cs:117`'s `Assert.Equal(1, chatClient.CallCount)`, which a multi-executor workflow will break.

**The `skill-framework` change must be a deliberate edit, not a drift.** Its "a skill never changes
the tool set" invariant is directly opposed to a handoff split: today `activate_skill` returns
guidance and the seven tools are unchanged, but under Phase 2 a skill that calls for retrieval has
to be able to reach the retrieval capability, and a skill that collects fields has to be able to
reach creation. Either that requirement is amended to scope the invariant to what the *skill
document* may assert (a skill describes a procedure; it cannot grant or remove capability) while
the orchestrator decides routing, or Phase 2 cannot honestly claim a skill "only adds
conversational guidance". Write the amended requirement as a delta against `skill-framework/spec.md`
in the Phase 2 change, with its scenarios restated — not as an edit to this change, and not by
letting the prose drift while the tests are being updated. The same applies to the two scenarios
above: "when a session is created" already needs rewording to "when a request is composed", which
this change makes true in the code but does not itself correct in the spec text.

## Risks / Trade-offs

- **`Instructions` duplication is undetectable by the existing suite** → nothing fails when the prompt starts accumulating a copy per turn; cost just climbs. Mitigation: the D7 guard test is written and run against the pre-migration code first, and its assertion (exactly one manifest section on turn 2) is what decides whether the migration proceeds. This is the single highest-risk item in the change.
- **A shrunken `CoreInstructions` weakens the universal guardrails' prominence** → zero-hallucination and language-match are the two rules that keep answers grounded, and burying them in a smaller block could change model behavior. Mitigation: they stay in `CoreInstructions` and are not moved; the change is verified against the live demo, not only against the test suite.
- **Moving creation-gate prose out of `CoreInstructions` could make the gate less salient** → the model reads a shorter prompt with the gate rule further from the top. Counter-argument: the gate is enforced in code and the archived `2026-09-25-harden-agent-tool-contract` already proved the prose version was unreliable, which is why the code gate exists. Accepted, and the live demo is the check.
- **Three classes instead of one increases the chance of a duplicate wire name at registration** → the very drift the shared-constant requirement exists to prevent. Mitigation: `ToolNames` holds every name, and a new layering assertion checks the three specialists partition the seven tools, so a duplicate or omission fails a test.
- **`SpecialistToolSet` could be reintroduced as a base class by a later change** → the annotated-method-group rule is the fragile part. Mitigation: `ToolSchemaTests` is the standing guard; it fails loudly if a registration ever routes through a lambda.
- **Spec drift already present in the repo** → `agent-framework-layering` and `skill-framework` both still say "four tools" after the fifth landed. This change fixes the text it touches (`agent-framework-layering`); the `skill-framework` count is corrected in Phase 2, when the tool partition actually changes. `AGENTS.md`'s tool-checklist is corrected here.
- **Phase 1 delivers cohesion but not least privilege** → after this change one agent still holds `create_requisition` and can still call it. The blast-radius reduction the change is motivated by is only realized in Phase 2. Accepted for Phase 1; if the actual driver is write-path risk rather than code organization, a narrow code-enforced write-authorization wrapper is independently shippable and does not depend on this refactor.
- **Phase 2 handoff reliability is unproven** → handoff adds a model decision point, and the archived `2026-09-25-harden-agent-tool-contract` recorded this model failing to call `activate_skill` in 4 of 6 live runs. Two routing decisions may degrade more than one. Mitigation: Phase 2 requires a live demo walkthrough, not a green suite.
- **Test churn hides a real assertion change** → the edits are mechanical, but mechanical edits to tests are exactly how a behavior regression gets waved through. Mitigation: the assertion-bearing files (`SkillFrameworkTests`, `RequisitionFlow`, `AgenticRetrievalTests`, `RagObservabilityReportTests`, `ItemSupplierCombinationTests`, and every `ToolSchemaTests` schema/result assertion) are deliberately left untouched, so the suite's behavioral coverage is unchanged by construction rather than by review.

## Migration Plan

- No database schema change, no EF migration, no data migration, no API change, no front-end change, no new NuGet package.
- Deploy order irrelevant: one build. Existing skill markdown needs no edit because wire names are unchanged.
- Sessions are in-memory and lost on restart, so the agent rename breaks no persisted state. Nothing else consumes `AgentName`.
- Rollback: revert the commit. The observability report files written during a session are identical before and after.

## Open Questions

- **Does MAF's `ChatHistoryProvider` persist the injected `Instructions` message into accumulated history?** Unanswerable from the package XML docs. The D7 guard test settles it empirically and gates the migration. This is the one item that can change the implementation approach.
- **Should the orchestrator's `AgentDescription` change when Phase 2 lands?** It is kept verbatim in Phase 1 because it reads correctly for a single agent that both answers and guides. Once specialists exist, the orchestrator's description becomes what a routing layer reads, and a retrieval-specific description may serve it better. Deferred to Phase 2.
- **Should the skill framework migrate to MAF's `AgentSkillsProvider`?** It is available in 1.20.0 and implements the same progressive-disclosure pattern with `load_skill`/`read_skill_resource`. The bespoke framework works and its specs are load-bearing. Out of scope here; worth a separate evaluation before Phase 2 rewrites the skill markdown anyway, since that change touches the same code.
- **Should Phase 2 adopt `WithCheckpointing` to make sessions survive restart?** Available in `Microsoft.Agents.AI.Workflows`, and it would fix the current in-memory session loss. It is orthogonal to the orchestrator split and would be its own change.
