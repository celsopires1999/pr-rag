## 1. Guard test for the prompt channel (do this first)

- [x] 1.1 Add a test in `ToolSchemaTests` (or a sibling) that runs two turns against the same `session_id` with the `FakeChatClient` and asserts how many times the skill-manifest section appears in `chatClient.LastPrompt` on turn 2 — capturing today's pre-migration behavior as the baseline
- [x] 1.2 Run that test against the current code and record the baseline count in a comment, so the post-migration assertion in 6.4 has a known "before" value rather than an assumed one

### Baseline measurement (1.1–1.2)

Wrote `backend/tests/PrRag.Tests/SystemPromptChannelTests.cs` (a sibling file — the concern is the
prompt *channel*, not the tool schema). Two facts recorded, both measured not assumed:

- **Baseline count is 1 on every turn** (turn 1, turn 2, each new session). No pre-existing
  duplication. The first run reported 2 and that was a bug in the test, not the code:
  `CoreInstructions` contains a backticked `<AVAILABLE_SKILLS>` inside the `activate_skill` bullet,
  so counting the header string also counted that prose reference. The marker is now the manifest
  *body* line `- create-purchase-requisition:`, which only exists where the catalog is rendered.
- **MAF's `ChatHistoryProvider` does persist the system message into history.** That is why the
  manifest currently survives follow-up turns despite being sent only on `created`. It is also
  exactly the duplication risk the `Instructions` migration carries: the history copy would sit
  alongside a freshly injected one on turn 2. 6.4 will now settle it empirically.

The unresolved question in `design.md`'s Open Questions is answered only by 6.4, not by this
baseline — the baseline shows no duplication exists *today*, which is the precondition for the
migration being a clean swap rather than an addition.

## 2. Shared tool names

- [x] 2.1 Create `PrRag.Application/Services/Agents/ToolNames.cs` with the seven public consts — `SearchByCodes`, `SearchSemantic`, `ActivateSkill`, `GetSuppliersByItem`, `CreateRequisitionDraft`, `ConfirmRequisitionDraft`, `CreateRequisition` — with the same wire-name values as today
- [x] 2.2 Repoint every reference inside `PurchaseRequisitionTools.cs` (7 declarations, 7 `RegisterFunction` calls, and all `RecordToolCall`/`LogResult`/message-interpolation uses) at `ToolNames.*`
- [x] 2.3 Repoint the test references: `ToolSchemaTests.cs` (lines 110, 128, 156-158, 202-203, 230), `GetSuppliersByItemTests.cs` (lines 100, 143), `AgentFrameworkLayeringTests.cs` (lines 73-79)
- [x] 2.4 Build and run the suite to confirm this step alone is a no-op refactor — build 0 errors, **79/79 tests pass** (77 pre-existing + the 2 new guard tests)

Note: the const names lost their `Tool` suffix (`SearchByCodesTool` → `ToolNames.SearchByCodes`)
because the suffix existed only to read well as a member of the tools class. The values are
unchanged, so this is not a wire-name change.

## 3. Shared registration mechanics

- [x] 3.1 Create `Services/Agents/Specialists/SpecialistToolSet.cs` holding the `List<AITool>`, the scoped `AgentTurnContext`, and an `ILogger`, exposing `Add(string wireName, Delegate handler)`, `Record(string name, IDictionary<string, object?> arguments)`, `Log(string name, string[] argumentNames, int resultCount, long startedAt)`, and `All`
- [x] 3.2 Move `RegisterFunction`, `RecordToolCall`, and `LogResult` onto it verbatim, keeping `AIFunctionFactory.Create(handler, new AIFunctionFactoryOptions { Name = wireName })` with **no** `Description` override, and preserving the existing structured log template
- [x] 3.3 Preserve the documented reason the annotated method group is registered instead of a lambda (the `MethodInfo`-derived schema) as a comment on `Add`, since that is the fragile invariant

## 4. Capability units

- [x] 4.1 Create `Services/Agents/Specialists/SpecialistDefinition.cs` as a record of `(string Id, string DisplayName, string ActionBlock, IList<AITool> Tools)`
- [x] 4.2 Create `RequisitionSearchSpecialist` with id `purchase-requisition-retrieval-specialist`; move `SearchByCodesAsync`, `SearchSemanticAsync`, and `GetSuppliersByItemAsync` into it verbatim, keeping the `IEmbeddingService`/`IPurchaseRequisitionRepository`/`AgentTurnContext` dependencies, the `RewrittenQuery` assignment, the `RetrievedItems` append, and the `[Description]` text on every method and parameter
- [x] 4.3 Create `RequisitionCreationSpecialist` with id `purchase-requisition-creation-specialist`; move `CreateRequisitionDraftAsync`, `ConfirmRequisitionDraftAsync`, `CreateRequisitionAsync`, `CreateRequisitionArguments`, `FindConflict`, and the item/supplier-combination check into it verbatim, keeping the confirmation gate, its ordering, every refusal message string character-for-character, and the `SkillSessionState.Clear`/`RequisitionDraftSessionState.Clear` calls
- [x] 4.4 Create `SkillActivationSpecialist` with id `purchase-requisition-skill-activation`; move `ActivateSkillAsync` into it verbatim with its `ISkillService` dependency and the "Unknown skill ... Available skills: ..." message unchanged
- [x] 4.5 Give each unit an `ActionBlock` const holding its `<ALLOWED_ACTIONS>` bullets, moved verbatim from `CoreInstructions`; place the creation-gate prose ("Never call `create_requisition` directly", "A field list is not consent") in the creation block, next to the code that enforces the gate
- [x] 4.6 Update the XML doc on each unit to state the tools it owns and, for the creation unit, that the gate lives in code rather than prompt text

Verified mechanically rather than by eye: a script brace-matches all eight moved members out of
both the old and new sources, normalizes whitespace and the `RecordToolCall`→`_tools.Record` /
`LogResult`→`_tools.Log` / `ToolNames.` renames, and diffs them. All eight bodies, the
`CreateRequisitionArguments` array, and all ten `ActionBlock` bullet lines match their originals
character-for-character. The check earned its keep — it caught a stray `$` interpolation prefix
I had introduced on the "not an explicit confirmation" message, which no compiler warning would
have flagged and which would have made the "character-for-character" claim in 4.3 false.

## 5. Prompt fragmentation

- [x] 5.1 Trim `AgentInstructions.CoreInstructions` to cross-cutting text only: identity, the ReAct loop, "do not output reasoning to the user", `<DATA_DICTIONARY>`, and the universal `<STRICT_GUARDRAILS>` (zero hallucination, language match, creation limits, data blindspots)
- [x] 5.2 Change `ComposeSystemPrompt` to take the manifest plus the catalog's action blocks and compose core text + action blocks + skills section, keeping the empty-manifest branch that omits the skills guide
- [x] 5.3 Move the "IMPORTANT: always check if there is a matching skill..." rule out of `CoreInstructions` and into the skill block beside `SkillsGuide`, fixing the contradiction where an empty manifest prompt still told the model to consult the catalog
- [x] 5.4 Rename `AgentInstructions.AgentName` to `purchase-requisition-orchestrator`; leave `AgentDescription` verbatim
- [x] 5.5 Confirm `AgentFrameworkLayeringTests.cs:49`'s `Assert.Contains(AgentInstructions.CoreInstructions, prompt)` still passes with the shrunken constant, and that the `No skills are available.` assertion at line 60 still passes

Two things worth recording about 5.1/5.5. `<ALLOWED_ACTIONS>`'s header and intro line were *not*
deleted — they now close `CoreInstructions`, with the capability units' bullets appended after
them, which is what makes "core text + action blocks + skills section" a plain concatenation.
That is the only reordering: a line-level diff confirms every other retained line is
byte-identical and in its original position, and that the only removals are the 7 tool bullets,
the required-parameters sub-bullet, the IMPORTANT rule, and the 2-line creation-gate prose.

`actionBlocks` is a **required** parameter, not an optional one with an empty default. An
optional default would let a caller silently ship a prompt whose `<ALLOWED_ACTIONS>` section has a
header and no bullets — the model would see seven undocumented tools and no compiler would object.

## 6. Prompt migration to agent instructions

- [x] 6.1 Inject `ISkillService` into `AgentRunService` and pass `instructions: AgentInstructions.ComposeSystemPrompt(manifest, catalog.ActionBlocks)` to the existing `AsAIAgent(...)` call — `instructions` is an optional parameter on the overload already in use
- [x] 6.2 Remove the `if (created)` system-message block from `ChatService.BuildTurnMessages` and drop the now-unused `created` parameter, updating its XML doc; keep the active-skill body injection and the user turn exactly as they are
- [x] 6.3 Keep `AgentRunService` `AddScoped` so the prompt is recomposed per request — this is what makes the manifest fresh
- [x] 6.4 Turn the 1.1 guard test into a regression assertion: turn 2's prompt must contain the skill-manifest section exactly once. **If this fails**, MAF persists injected instructions into history — in that case revert 6.1/6.2 and instead recompose the prompt inside `BuildTurnMessages` on every turn rather than only when `created`, which satisfies the same requirement with no duplication risk
- [x] 6.5 Add a test proving a `SkillsWatcherService`-style manifest reload reaches an in-flight session's next turn, which is the spec drift this migration closes

### Where 6.1 landed, and why 6.4's fallback did not apply

**`ChatClientAgent` does not put `Instructions` into the message list.** It passes them as
`ChatOptions.Instructions`. Verified with a throwaway probe agent wired to an instrumented
`IChatClient` that dumped both channels, because the guard test first read `Expected: 1,
Actual: 0` and "the prompt vanished" and "the fake cannot see that channel" are indistinguishable
from the assertion alone.

Two consequences, both of which the task did not anticipate:

1. **The fallback branch in 6.4 is unreachable.** The hazard it guards against is a history copy
   of the system prompt sitting beside a freshly injected one on turn 2. That requires the
   instructions to enter the message history, and they never do. 6.1/6.2 stand as written.
2. **The fake had a blind spot.** `FakeChatClient` joined only the message texts, so it could not
   observe the system prompt at all after the migration — it would have reported every
   prompt-driven regression as "no prompt was sent". `BuildPromptText` now prepends
   `ChatOptions.Instructions`. All existing `LastPrompt` assertions are `Assert.Contains`, so
   widening it could not turn a passing test red; it only makes a previously invisible channel
   assertable.

The open question in `design.md` is therefore answered: instructions are recomposed per request
and delivered out of band, so the manifest is fresh every turn and cannot accumulate.

Freshness is scoped, though: `AgentRunService` composes the prompt when constructed, so the
guarantee holds per request, not per run within one scope. That matches production (one scope per
request) and 6.3's reasoning, but 6.5's test is written in that shape deliberately — a second
scope continuing the same session — and says so. `ChatClientAgentRunOptions` also exposes
`Instructions`, so per-*run* recomposition is available if a future change makes one scope serve
many turns.

## 7. Catalog and composition

- [x] 7.1 Create `ISpecialistCatalog` exposing the capability definitions, the combined tool list, and the combined action blocks
- [x] 7.2 Create `SpecialistCatalog` resolving the three units and aggregating them; assert the partition — exactly the seven `ToolNames` values, none duplicated across units

**The first cut of this was wrong, and the partition assertion is what caught it.** `Add`
returned `void` and each unit pointed its `Definition.Tools` at the shared `SpecialistToolSet.All`
— so all three definitions advertised all seven tools and the catalog aggregated 21. DI hands every
unit the *same* scoped `SpecialistToolSet`, so a single shared list cannot represent a partition.
`Add` now returns the `AITool` it created and each unit keeps its own `_ownedTools`.

Rather than leave that as a test-only guard, `SpecialistCatalog` now throws at composition time if
the union of the per-unit lists is not set-equal to the shared registered set — a unit that
registers a tool without claiming it would otherwise silently drop it from the agent.
- [x] 7.3 Rewire `AgentRunService` to build `ChatOptions.Tools` from `catalog.AllTools`, leaving `ToolMode = ChatToolMode.Auto` unchanged
- [x] 7.4 Update `DependencyInjection.cs`: replace `AddScoped<PurchaseRequisitionTools>()` with scoped registrations for the three units and `ISpecialistCatalog`
- [x] 7.5 Delete `PurchaseRequisitionTools.cs` and confirm no reference to it remains anywhere in the solution

`ISkillService` also left `ChatService` as a dead dependency once 6.2 removed the only call to it,
so its field, constructor parameter, and assignment were removed too. `ChatService` now depends on
`IAgentRunService`, `IAgentSessionStore`, `AgentTurnContext`, `IRagReportWriter`, a logger, and
`RagSettings` — it no longer knows a prompt exists.

`SpecialistToolSet` takes `ILogger<SpecialistToolSet>` rather than the bare `ILogger` the task
specified. The non-generic `ILogger` is not registered by default, so the literal reading failed
37 tests with `Unable to resolve service for type 'ILogger'`. The generic form is auto-registered,
matches the old `ILogger<PurchaseRequisitionTools>`, and keeps one log category for all tool
logging, as before.

## 8. Test updates

- [x] 8.1 Rework `ToolSchemaTests.Functions()` (line 459) to resolve `ISpecialistCatalog` and read the combined tool list instead of `PurchaseRequisitionTools`
- [x] 8.2 Rework `AgentFrameworkLayeringTests.Purchase_requisition_tools_expose_the_fixed_tools` (line 62) to resolve the catalog, assert the same seven names against `ToolNames`, and add the partition assertion from 7.2
- [x] 8.3 Update `AgentFrameworkLayeringTests` prompt-composition calls (lines 47, 57) for the new `ComposeSystemPrompt` signature
- [x] 8.4 Leave `SkillFrameworkTests`, `RequisitionFlow`, `AgenticRetrievalTests`, `RagObservabilityReportTests`, `ItemSupplierCombinationTests`, and every `ToolSchemaTests` schema/description/result-shape assertion untouched — they are the evidence that this refactor changed no behavior
- [x] 8.5 Run the full suite and confirm no assertion needed an intent change; if one did, treat that as a behavior regression and stop rather than rewriting the assertion

**84/84 pass, and no existing assertion needed an intent change.** Not one was rewritten to
accommodate the refactor, which is the actual evidence for "this changed no behavior".

`git diff` confirms 8.4 held. `SkillFrameworkTests`, `RequisitionFlow`, `AgenticRetrievalTests`,
`RagObservabilityReportTests`, and `ItemSupplierCombinationTests` are byte-for-byte untouched.
`ToolSchemaTests` moves only in wiring — `PurchaseRequisitionTools.XTool` → `ToolNames.X` at the
call sites and the `Functions()` helper resolving `ISpecialistCatalog.AllTools` — with every
schema, description, and result-shape assertion intact. (`GetSuppliersByItemTests` is modified,
but that was 2.3's explicit instruction to repoint its const references, not a behavior edit.)

Test count went 79 → 84: the two guard tests from group 1, plus the reload test from 6.5, plus
three new prompt/catalog tests, minus the one obsolete `Purchase_requisition_tools_expose_the_fixed_tools`
that 8.2 replaced.

## 9. Documentation

- [x] 9.1 Update `AGENTS.md`'s "Adding an agent tool" gotcha: the four edits become five — handler, shared wire-name const, the owning capability's `ActionBlock` bullet (not `CoreInstructions`), the partition assertion, and the layering name assertion — keeping the warning about registering the method itself rather than a forwarding lambda
- [x] 9.2 Add a note to `AGENTS.md` recording that Phase 2 is a handoff workflow built on `Microsoft.Agents.AI.Workflows` presenting itself as an `AIAgent`, that `IAgentRunService` is the seam that must not change, and that `Id` and `Name` must split in Phase 2 for telemetry stability
- [x] 9.3 Note in the design doc's Phase 2 scope that the `skill-framework` "a skill never changes the tool set" requirement must be modified deliberately, not drifted into

## 10. Verify

- [x] 10.1 Build as the `vscode` user in the devcontainer: `docker compose exec -u vscode -w /workspaces/backend devcontainer dotnet build /workspaces/backend/PrRag.sln -c Debug`
- [x] 10.2 Run the suite against the compose `db` service: `docker compose exec -u vscode -w /workspaces/backend devcontainer dotnet test /workspaces/backend/tests/PrRag.Tests/PrRag.Tests.csproj` — all tests pass
- [x] 10.3 Validate the change: `openspec validate modularize-agent-capabilities --strict`
- [x] 10.4 Re-run the demo with a real OpenAI key (`docker compose --profile demo up`) and walk both flows: a plain retrieval question, and the full create-requisition path end to end. The prompt has been restructured and the system prompt moved channels, so model behavior must be confirmed live — a green suite does not cover this. Specifically confirm: `activate_skill` still fires on a natural phrasing, the model still calls the three creation tools in order, and the confirmation gate still holds against an adversarial skip-the-confirmation turn
- [x] 10.5 Record the live findings in this file, including whether the model needed a second turn to recover anywhere, since that is the signal for whether Phase 1's prompt restructuring is safe to build on

### Live findings (10.5)

Run against the real model (`gpt-4o-mini`, demo profile). `API_PORT` was set because the
`devcontainer` service already binds host 8080 and the `api` service would otherwise collide with
it — worth knowing before anyone concludes the demo is broken.

**What works, and is the point of the restructure:**

- `search_by_codes`, `search_semantic`, and `get_suppliers_by_item` all fire correctly, in the right
  turn, with correct arguments. `RagQueryReport` is unchanged and records them
  (`{"Name":"search_semantic","Arguments":{"query":"purchases for packaging and shipping"}}`).
- **`activate_skill` still fires on a plain phrasing.** "I need to create a purchase requisition"
  activated `create-purchase-requisition` with no tool names in the question. This is the load-bearing
  check for the `ActionBlock` split — the skill bullet lives in `SkillActivationSpecialist` now, and the
  model still finds and uses it.
- The three creation tools fire in the right order when the model commits: `activate_skill` →
  `search_by_codes` → `create_requisition_draft`, and the draft is staged and presented
  (`RequisitionDraftStaged`/`Presented` true, `Confirmed`/`Persisted` false).
- **The core instructions really do reach the model through the new channel.** Probed with two rules
  that exist *only* in `CoreInstructions` and nowhere else: asked in Portuguese, it answered in
  Portuguese (Language Match); asked which requisition was newest, it answered exactly "I don't have
  enough information to answer that." (Data Blindspots). So `ChatOptions.Instructions` is not being
  dropped on the way to OpenAI.

**The confirmation gate held in every single live run.** 14 runs, 0 confirmations, 0 persistences, DB
requisition count unmoved at 3000. Including the run where the model reached for `create_requisition`
with no confirmed draft and was refused.

**One thing did not work, and it is not a regression.** The model cannot reliably carry a staged draft
across a turn boundary, so the create path did not reach persistence. On "yes" it re-activates the skill
and re-asks for the supplier code, or re-stages the draft (which by design discards any earlier
confirmation), so the flow loops. One attempt did reach `confirm_requisition_draft` and then derailed
into six spurious lookups; another **hallucinated a draft** (ITM-67890 "Office Chair", SUP-12345) in
response to being told to create it.

I attributed this rather than assuming it. I restored `backend/src` to `HEAD`, rebuilt, and ran the
identical flow against the pre-change code: answering "yes" produced "I don't have enough information to
answer that." with **zero** tool calls, and on the next turn it called `confirm_requisition_draft` then
`create_requisition` and was correctly refused. A separate history probe makes it plainer — after a
grounded turn 1 listing ten suppliers, "of the suppliers you just listed, which is first
alphabetically?" returned "I don't have enough information to answer that." with zero tool calls, on the
new code and with the identical failure mode as the baseline. Message-level history is intact
(`Full_history_carried_across_turns` passes and 6.5's reload test passes), so this is the model not
using the history it is given.

So: **pre-existing, and if anything less bad after the change.** Two live-only quality signals worth
carrying into Phase 2 rather than re-discovering: the over-eager `activate_skill` re-activation, and the
hallucinated draft. Both are the kind of thing a dedicated creation specialist holding the draft in its
own turn context would address directly, which is a point in favour of the split rather than against it.

