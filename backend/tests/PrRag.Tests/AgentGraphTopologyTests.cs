using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PrRag.Application.Abstractions;
using PrRag.Application.Domain;
using PrRag.Application.DTOs;
using PrRag.Application.Services.Agents;
using PrRag.Application.Services.Agents.Specialists;
using PrRag.Infrastructure.Persistence;
using Xunit;

namespace PrRag.Tests;

/// <summary>
/// The graph's shape, asserted where it is actually load-bearing: on the tools
/// each agent is offered at run time, on the answer a participant hands back,
/// and on the report a routed turn produces.
/// </summary>
/// <remarks>
/// These are deliberately behavioural. Asserting the catalog's definitions would
/// pass even if the composer wired the tools onto the wrong agent, because the
/// definitions and the composition are separate places. The fake records the
/// tools each request was issued with, so these tests read what the model
/// actually saw.
/// </remarks>
public class AgentGraphTopologyTests : IAsyncLifetime
{
    private static readonly string[] RetrievalTools =
        [ToolNames.SearchByCodes, ToolNames.SearchSemantic, ToolNames.GetSuppliersByItem];

    private static readonly string[] CreationTools =
        [ToolNames.CreateRequisitionDraft, ToolNames.ConfirmRequisitionDraft, ToolNames.CreateRequisition];

    /// <summary>
    /// Everything the orchestrator still owns. Written out rather than computed
    /// as the complement of the two specialists: a complement passes silently if a
    /// fourth tool is handed to the front door, which is the failure this change
    /// is about.
    /// </summary>
    private static readonly string[] OrchestratorTools = [ToolNames.ActivateSkill];

    private static string ConnectionTemplate => TestDatabase.ConnectionStringTemplate;

    private readonly string _dbName = $"prrag_test_{Guid.NewGuid():N}";
    private string _connectionString = null!;
    private ServiceProvider? _provider;
    private string _dataDir = null!;

    public async Task InitializeAsync()
    {
        _connectionString = $"{ConnectionTemplate};Database={_dbName}";
        (_provider, _, _dataDir) = IntegrationServiceFactory.Create(_connectionString);
        await TestDatabase.MigrateAndReloadTypesAsync(_provider);

        await WriteJsonAsync(
        [
            new PurchaseRequisitionImport
            {
                PurchaseRequisition = "PR00000001",
                SupplierCode = "SUP000001",
                SupplierName = "Acme Industrial Supply",
                Item = "ITM0001",
                ItemName = "Hydraulic Pump",
                Description = "Procurement of Hydraulic Pump for maintenance operations.",
            },
        ]);

        using var scope = _provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IIngestionService>().IngestAsync();
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
            await TestDatabase.DropDatabaseAsync(_dbName);
        }

        if (!string.IsNullOrEmpty(_dataDir) && Directory.Exists(_dataDir))
        {
            Directory.Delete(_dataDir, recursive: true);
        }
    }

    /// <summary>
    /// A routed turn must reach the retrieval agent holding the read tools, and
    /// that agent must not be offered a single write tool.
    /// </summary>
    [Fact]
    public async Task A_routed_turn_is_offered_only_the_retrieval_tools()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.HandOffToRetrieval(scope.ServiceProvider);
        chatClient.ToolCall = new FunctionCallContent(
            "call_1",
            ToolNames.SearchByCodes,
            new Dictionary<string, object?> { ["suppliers"] = new[] { "SUP000001" } });

        await chat.AnswerAsync(new ChatRequest
        {
            Question = "what is requisition from supplier SUP000001?",
            TopK = 5,
            MinSimilarity = 0,
        });

        var offered = LastOffered(chatClient);
        Assert.Equal(RetrievalTools.OrderBy(n => n, StringComparer.Ordinal), offered.OrderBy(n => n, StringComparer.Ordinal));

        // The write tools live on a different agent. If routing ever handed the
        // participant the combined list, this is the assertion that catches it.
        Assert.Empty(ToolNames.Writes.Intersect(offered, StringComparer.Ordinal));
    }

    /// <summary>
    /// The converse, which is now the more important half: a creation turn must
    /// reach the agent that holds the write tools, and that agent must not be
    /// offered a read tool — the confirmation gate is a write path, and an agent
    /// that can look things up mid-gate is an agent that can talk itself past it.
    /// </summary>
    [Fact]
    public async Task A_creation_turn_is_offered_only_the_creation_tools()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        // The turn starts at the orchestrator, which holds no write tool, so the
        // handoff is what makes the write path reachable. That is the point: the
        // first turn of a creation request cannot be served by the front door.
        RequisitionFlow.OnCreationAgent(chatClient, scope.ServiceProvider, RequisitionFlow.Draft());

        await chat.AnswerAsync(new ChatRequest
        {
            Question = "create a purchase requisition for ITM0001 from SUP000001",
            TopK = 5,
            MinSimilarity = 0,
        });

        var offered = LastOffered(chatClient);
        Assert.Equal(CreationTools.OrderBy(n => n, StringComparer.Ordinal), offered.OrderBy(n => n, StringComparer.Ordinal));
        Assert.Empty(ToolNames.ReadOnly.Intersect(offered, StringComparer.Ordinal));
    }

    /// <summary>
    /// The orchestrator's own turn: it fronts the conversation, so it must still be
    /// the entry point, and it must be offered exactly what it retains.
    /// </summary>
    [Fact]
    public async Task An_ordinary_turn_is_offered_only_the_orchestrator_tools()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        // No handoff: this turn is about the orchestrator's own tool, so with
        // auto-handoff on the fake would route it away and the assertion below
        // would read the wrong agent's tool set.
        chatClient.AutoHandoff = false;
        chatClient.ToolCall = new FunctionCallContent(
            "call_1",
            ToolNames.ActivateSkill,
            new Dictionary<string, object?> { ["name"] = "create-purchase-requisition" });

        await chat.AnswerAsync(new ChatRequest
        {
            Question = "activate the create purchase requisition skill",
            TopK = 5,
            MinSimilarity = 0,
        });

        var offered = LastOffered(chatClient);
        Assert.Equal(
            OrchestratorTools.OrderBy(n => n, StringComparer.Ordinal),
            offered.OrderBy(n => n, StringComparer.Ordinal));
    }

    /// <summary>
    /// Across the three agents the offered sets must be exactly the seven wire
    /// names, partitioned. A tool on no agent is unreachable; a tool on two gives
    /// the model two copies, and on the wrong pair it hands a write tool to a read
    /// capability.
    /// </summary>
    [Fact]
    public async Task The_agents_partition_the_seven_tool_names_between_them()
    {
        var orchestrator = await OfferedToolsForAsync(participant: null);
        var retrieval = await OfferedToolsForAsync(participant: AgentIds.Retrieval);
        var creation = await OfferedToolsForAsync(participant: AgentIds.Creation);

        var sets = new[] { orchestrator, retrieval, creation };

        var union = sets.SelectMany(s => s).Distinct(StringComparer.Ordinal).ToList();
        Assert.Equal(7, union.Count);

        for (var i = 0; i < sets.Length; i++)
        {
            for (var j = i + 1; j < sets.Length; j++)
            {
                Assert.Equal(
                    sets[i].Intersect(sets[j], StringComparer.Ordinal),
                    Array.Empty<string>());
            }
        }
    }

    /// <summary>
    /// The participant's answer is the answer. A workflow that wrapped it in the
    /// orchestrator's own prose would still look correct to a reader, so this
    /// asserts the text is byte-identical to what the participant produced.
    /// </summary>
    [Fact]
    public async Task A_participants_answer_reaches_the_caller_unparaphrased()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        const string SpecialistAnswer = "PR00000001 from Acme Industrial Supply.";

        chatClient.AutoHandoff = true;
        chatClient.ToolCall = new FunctionCallContent(
            "call_1",
            ToolNames.SearchByCodes,
            new Dictionary<string, object?> { ["suppliers"] = new[] { "SUP000001" } });
        chatClient.Answer = SpecialistAnswer;

        var response = await chat.AnswerAsync(new ChatRequest
        {
            Question = "what is requisition from supplier SUP000001?",
            TopK = 5,
            MinSimilarity = 0,
        });

        Assert.Equal(SpecialistAnswer, response.Answer);
    }

    /// <summary>
    /// Which agent authors the answer once a handoff has moved the turn.
    ///
    /// <para>
    /// This is not a curiosity. A handoff turn produces one answer from a chain of
    /// agents, and a report that records <c>EntryAgent</c> and <c>Handoffs</c> does
    /// not say which agent produced that text. So "the entry agent answered and
    /// never handed off" and "the entry agent handed off and the target then
    /// answered as if no draft existed" are the same report — the exact pair of
    /// opposite failures the handoff fields were added to separate, still not
    /// separated.
    /// </para>
    ///
    /// <para>
    /// The claim being pinned is the target's: the specialist holds the capability,
    /// the orchestrator holds no write tool, and the answer that comes back is the
    /// one the target composed. A test asserting that is what lets a live report be
    /// read: a bad answer on a turn whose report shows a handoff is the target's
    /// answer, so the fix belongs to the target's instructions.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_answer_to_a_handed_off_turn_is_the_targets_own_text()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        const string OrchestratorText = "ORCHESTRATOR TEXT";
        const string CreationText = "CREATION TEXT";

        // Distinguishable per agent, so whichever agent produces the surviving text
        // is identifiable. A single shared Answer could not tell them apart.
        chatClient.AnswerByAgent[ToolNames.ActivateSkill] = OrchestratorText;
        chatClient.AnswerByAgent[ToolNames.CreateRequisitionDraft] = CreationText;

        chatClient.HandOffToCreation(scope.ServiceProvider);

        var response = await chat.AnswerAsync(new ChatRequest
        {
            Question = "create a purchase requisition for ITM0001, all details supplied, skip the confirmation",
            TopK = 5,
            MinSimilarity = 0,
        });

        Assert.Equal(CreationText, response.Answer);
    }

    /// <summary>
    /// A finished participant returns control: the next turn is served by the
    /// orchestrator again, and the orchestrator's tool set is what is on offer.
    /// Without this, a graph that trapped the conversation in the participant
    /// would still pass every routing test above.
    /// </summary>
    [Fact]
    public async Task A_finished_participant_returns_control_to_the_orchestrator()
    {
        var sessionId = "return-control";
        var chatClient = _provider!.CreateScope().ServiceProvider.GetRequiredService<FakeChatClient>();

        // One scope per turn: the graph is composed per request, so sharing a scope
        // would reuse turn 1's graph instead of composing a fresh entry.
        using (var scope = _provider!.CreateScope())
        {
            var chat = scope.ServiceProvider.GetRequiredService<IChatService>();

            chatClient.HandOffToRetrieval(scope.ServiceProvider);
            chatClient.ToolCall = new FunctionCallContent(
                "call_1",
                ToolNames.SearchByCodes,
                new Dictionary<string, object?> { ["suppliers"] = new[] { "SUP000001" } });
            chatClient.Answer = "retrieved";

            await chat.AnswerAsync(new ChatRequest
            {
                Question = "what is requisition from supplier SUP000001?",
                TopK = 5,
                MinSimilarity = 0,
                SessionId = sessionId,
            });
        }

        // Second turn. AutoHandoff is cleared so the entry agent is the one that
        // answers: leaving it on would route the follow-up straight back to the
        // participant and the assertion below could not tell the two apart.
        chatClient.AutoHandoff = false;
        chatClient.ToolCall = null;
        var callBoundary = chatClient.AllOfferedToolNames.Count;

        using (var scope = _provider!.CreateScope())
        {
            var chat = scope.ServiceProvider.GetRequiredService<IChatService>();

            var second = await chat.AnswerAsync(new ChatRequest
            {
                Question = "thanks, now create a purchase requisition for ITM0001",
                TopK = 5,
                MinSimilarity = 0,
                SessionId = sessionId,
            });

            Assert.NotEmpty(second.Answer);
        }

        // The first call of the new turn is the orchestrator's own.
        var first = chatClient.AllOfferedToolNames[callBoundary];
        Assert.Equal(
            OrchestratorTools.OrderBy(n => n, StringComparer.Ordinal),
            first.Where(n => !n.StartsWith("handoff_to_", StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal));
    }

    /// <summary>
    /// Streaming must actually emit. The rest of this suite only ever reads a
    /// completed answer, so a regression that left the SSE channel silent would
    /// pass all of it.
    /// </summary>
    [Fact]
    public async Task The_graph_emits_streaming_response_updates()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        const string Answer = "streamed answer for SUP000001";

        chatClient.HandOffToRetrieval(scope.ServiceProvider);
        chatClient.Answer = Answer;
        chatClient.ToolCall = new FunctionCallContent(
            "call_1",
            ToolNames.SearchByCodes,
            new Dictionary<string, object?> { ["suppliers"] = new[] { "SUP000001" } });

        var deltas = new List<string>();
        await foreach (var delta in chat.StreamAsync(new ChatStreamRequest
        {
            Question = "what is requisition from supplier SUP000001?",
            TopK = 5,
            MinSimilarity = 0,
        }))
        {
            deltas.Add(delta);
        }

        Assert.NotEmpty(deltas);
        Assert.All(deltas, d => Assert.False(string.IsNullOrWhiteSpace(d)));
        Assert.Equal(Answer, string.Concat(deltas));
    }

    /// <summary>
    /// A routed turn must produce the same report shape as an unrouted one.
    /// Routing is exactly the kind of change that quietly adds or drops a field,
    /// and the report is what the observability gate reads.
    /// </summary>
    [Fact]
    public async Task A_routed_turn_reports_the_same_fields_as_an_unrouted_one()
    {
        var unrouted = await ReportPropertiesForAsync(participant: null);
        var routed = await ReportPropertiesForAsync(participant: AgentIds.Retrieval);
        var creation = await ReportPropertiesForAsync(participant: AgentIds.Creation);

        Assert.Equal(unrouted.OrderBy(p => p, StringComparer.Ordinal), routed.OrderBy(p => p, StringComparer.Ordinal));
        Assert.Equal(unrouted.OrderBy(p => p, StringComparer.Ordinal), creation.OrderBy(p => p, StringComparer.Ordinal));
    }

    /// <summary>
    /// The report must still name the tool the participant called. This is the
    /// assertion that would have caught the earlier misreading where a report
    /// looked as if it recorded no tool name at all.
    /// </summary>
    [Fact]
    public async Task A_routed_turn_records_the_participants_tool_call()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.HandOffToRetrieval(scope.ServiceProvider);
        chatClient.ToolCall = new FunctionCallContent(
            "call_1",
            ToolNames.SearchByCodes,
            new Dictionary<string, object?> { ["suppliers"] = new[] { "SUP000001" } });

        await chat.AnswerAsync(new ChatRequest
        {
            Question = "what is requisition from supplier SUP000001?",
            TopK = 5,
            MinSimilarity = 0,
        });

        var context = scope.ServiceProvider.GetRequiredService<AgentTurnContext>();
        var call = Assert.Single(context.ToolCalls);
        Assert.Equal(ToolNames.SearchByCodes, call.Name);
        Assert.NotNull(call.Arguments);
    }

    private static List<string> LastOffered(FakeChatClient chatClient)
    {
        var offered = chatClient.LastOfferedToolNames
            .Where(n => !n.StartsWith("handoff_to_", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(offered);
        return offered;
    }

    /// <summary>
    /// The creation gate spans turns, so the turn that resolves it must enter at
    /// the agent that holds the write tools.
    /// </summary>
    /// <remarks>
    /// Turn one stages a draft and hands off to creation, which is the graph's
    /// normal path. Turn two is the interesting one: the application already knows
    /// a draft is staged and unconfirmed, so the write path must be reachable
    /// without the model re-deriving that. Asserting the turn's first call is the
    /// creation agent's is the observation that distinguishes "entered at
    /// creation" from "entered at the orchestrator and handed off" — a handoff
    /// could eventually get there, but only if the model chose to make one, and
    /// that is exactly the dependency being removed.
    /// </remarks>
    [Fact]
    public async Task A_turn_with_a_pending_draft_enters_at_the_creation_agent()
    {
        var sessionId = "pending-draft-entry";
        var chatClient = _provider!.CreateScope().ServiceProvider.GetRequiredService<FakeChatClient>();

        // One turn is one request, and the graph is composed per request, so each
        // turn gets its own scope. Sharing one would compose the graph once for
        // turn 1 and reuse it for turn 2, which is exactly the state-dependent
        // composition under test.
        using (var draftTurn = _provider.CreateScope())
        {
            var chat = draftTurn.ServiceProvider.GetRequiredService<IChatService>();
            RequisitionFlow.OnCreationAgent(chatClient, draftTurn.ServiceProvider, RequisitionFlow.Draft());

            await chat.AnswerAsync(new ChatRequest
            {
                SessionId = sessionId,
                Question = "create a purchase requisition for ITM0001 from SUP000001",
                TopK = 5,
                MinSimilarity = 0,
            });
        }

        Assert.Contains("\"staged\":true", chatClient.LastToolResultJson().Replace(" ", ""));

        // Turn 2: the user's "yes". No handoff is scripted and none could help —
        // the orchestrator holds no write tool, so if the turn entered there the
        // confirm and create calls would never be dispatched at all.
        chatClient.ResetScript();
        var boundary = chatClient.AllOfferedToolNames.Count;

        using (var second = _provider.CreateScope())
        {
            var chat = second.ServiceProvider.GetRequiredService<IChatService>();
            RequisitionFlow.OnCreationAgent(chatClient, second.ServiceProvider,
                RequisitionFlow.Confirm(),
                RequisitionFlow.Create());

            await chat.AnswerAsync(new ChatRequest
            {
                SessionId = sessionId,
                Question = "yes, go ahead",
                TopK = 5,
                MinSimilarity = 0,
            });
        }

        var first = chatClient.AllOfferedToolNames[boundary];

        // Entered at creation: the write tools are on offer. An orchestrator-entry
        // graph would have offered activate_skill and none of them. A handoff tool
        // is still offered, and should be — creation keeps its edge to retrieval so
        // it can pass on a lookup question.
        Assert.Equal(
            CreationTools.OrderBy(n => n, StringComparer.Ordinal),
            first.Where(n => !n.StartsWith("handoff_to_", StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal));
        Assert.DoesNotContain(ToolNames.ActivateSkill, first);
        Assert.Single(first, n => n.StartsWith("handoff_to_", StringComparison.Ordinal));

        // And the turn did what the user asked, which is the reason any of this
        // exists.
        Assert.Contains("Requisition created", chatClient.LastToolResultJson());

        using var verify = _provider!.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<PrRagDbContext>();
        Assert.Single(await db.CreatedRequisitions.AsNoTracking().ToListAsync());
    }

    /// <summary>
    /// A confirmed-but-unwritten draft is the most urgent state in the system, and
    /// it is still "a draft is staged" as far as the entry point is concerned.
    /// </summary>
    /// <remarks>
    /// If the entry point keyed on <em>unconfirmed</em> drafts, a write that failed
    /// after a successful confirmation would be routed to an agent holding no
    /// write tool, and the user's confirmed requisition would be stuck behind a
    /// model that has no way to finish it.
    /// </remarks>
    [Fact]
    public async Task A_turn_with_a_confirmed_but_unwritten_draft_still_enters_at_creation()
    {
        var sessionId = "confirmed-unwritten-entry";
        var chatClient = _provider!.CreateScope().ServiceProvider.GetRequiredService<FakeChatClient>();

        // Turn 1 stages and confirms; turn 2 persists. The persist is not
        // scripted on turn 1, so the session is left holding a confirmed draft
        // with no row behind it.
        using (var scope = _provider.CreateScope())
        {
            var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
            RequisitionFlow.OnCreationAgent(chatClient, scope.ServiceProvider,
                RequisitionFlow.Draft(),
                RequisitionFlow.Confirm());

            await chat.AnswerAsync(new ChatRequest
            {
                SessionId = sessionId,
                Question = "create a purchase requisition for ITM0001 from SUP000001, yes that is right",
                TopK = 5,
                MinSimilarity = 0,
            });

            Assert.True(RequisitionDraftSessionState
                .Read(scope.ServiceProvider.GetRequiredService<IAgentSessionStore>().GetOrCreate(sessionId))
                .HasConfirmedDraft);
        }

        chatClient.ResetScript();
        var boundary = chatClient.AllOfferedToolNames.Count;

        using (var scope = _provider.CreateScope())
        {
            var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
            RequisitionFlow.OnCreationAgent(chatClient, scope.ServiceProvider, RequisitionFlow.Create());

            await chat.AnswerAsync(new ChatRequest
            {
                SessionId = sessionId,
                Question = "finish it",
                TopK = 5,
                MinSimilarity = 0,
            });
        }

        var first = chatClient.AllOfferedToolNames[boundary];
        Assert.DoesNotContain(ToolNames.ActivateSkill, first);
        Assert.Contains(ToolNames.CreateRequisition, first);
        Assert.Contains("Requisition created", chatClient.LastToolResultJson());
    }

    /// <summary>
    /// The recorded consequence of entering at a specialist: a pending draft puts
    /// <c>activate_skill</c> out of reach for that turn.
    /// </summary>
    /// <remarks>
    /// The orchestrator is the only agent that holds the skill manifest, because
    /// it is the only one that can activate a skill, so a turn that enters at
    /// creation cannot offer activation. That is a real loss of function, and it
    /// is the safe direction: the alternative is letting a new skill's guidance
    /// re-plan a write the user has already confirmed. Pinned here so the trade is
    /// a decision on record rather than an accident of the graph's shape.
    /// </remarks>
    [Fact]
    public async Task A_skill_cannot_be_activated_while_a_draft_is_pending()
    {
        var sessionId = "no-skill-while-pending";
        var chatClient = _provider!.CreateScope().ServiceProvider.GetRequiredService<FakeChatClient>();

        using (var scope = _provider.CreateScope())
        {
            var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
            RequisitionFlow.OnCreationAgent(chatClient, scope.ServiceProvider, RequisitionFlow.Draft());

            await chat.AnswerAsync(new ChatRequest
            {
                SessionId = sessionId,
                Question = "create a purchase requisition for ITM0001 from SUP000001",
                TopK = 5,
                MinSimilarity = 0,
            });
        }

        var boundary = chatClient.AllOfferedToolNames.Count;

        // A scripted activation on the pending-draft turn. The tool is not on
        // offer, so nothing dispatches it — the model has no way to ask.
        chatClient.ResetScript();
        chatClient.AutoHandoff = false;
        chatClient.ScriptFor(ToolNames.ActivateSkill, new FunctionCallContent(
            "call_1",
            ToolNames.ActivateSkill,
            new Dictionary<string, object?> { ["name"] = "create-purchase-requisition" }));

        string answer;
        using (var scope = _provider.CreateScope())
        {
            var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
            var response = await chat.AnswerAsync(new ChatRequest
            {
                SessionId = sessionId,
                Question = "activate the create purchase requisition skill",
                TopK = 5,
                MinSimilarity = 0,
            });

            answer = response.Answer;
        }

        Assert.NotEmpty(answer);

        var offered = chatClient.AllOfferedToolNames[boundary];
        Assert.DoesNotContain(ToolNames.ActivateSkill, offered);

        // The draft survived the turn untouched, so the gate is still where the
        // user left it rather than silently reset by a turn that could not act.
        Assert.Contains("\"staged\":true", chatClient.LastToolResultJson().Replace(" ", ""));
    }

    /// <summary>
    /// The entry point is a pure function of recorded state, so it is asserted
    /// without a chat turn: no provider, no database, no graph.
    /// </summary>
    [Fact]
    public void The_entry_point_follows_the_draft_state()
    {
        Assert.Equal(AgentIds.Orchestrator, AgentGraphComposer.ResolveEntryPoint(null));
        Assert.Equal(
            AgentIds.Orchestrator,
            AgentGraphComposer.ResolveEntryPoint(new AgentSessionState()));

        var draft = new RequisitionDraft("SUP000001", "ITM0001", "Hydraulic pump.", 3m, "2026-10-01", "Ana Souza");

        var staged = new AgentSessionState();
        RequisitionDraftSessionState.Stage(staged, draft);
        Assert.Equal(AgentIds.Creation, AgentGraphComposer.ResolveEntryPoint(staged));

        RequisitionDraftSessionState.MarkConfirmed(staged);
        Assert.Equal(AgentIds.Creation, AgentGraphComposer.ResolveEntryPoint(staged));

        // A written requisition clears the draft, so the next turn is an ordinary
        // one and the orchestrator is back in front.
        RequisitionDraftSessionState.Clear(staged);
        Assert.Equal(AgentIds.Orchestrator, AgentGraphComposer.ResolveEntryPoint(staged));
    }

    /// <summary>
    /// Entering at creation on a pending draft must not turn every unrelated
    /// question into a write attempt.
    /// </summary>
    /// <remarks>
    /// The entry point is chosen from the draft's state, not from the topic of the
    /// message, so the failure this guards against is a session that can no longer
    /// ask anything else. Turn two is deliberately off-topic and the model is left
    /// to improvise: it holds the write tools, so if anything it is more likely to
    /// call one, and the draft is still unconfirmed, so the gate must refuse.
    /// </remarks>
    [Fact]
    public async Task An_unrelated_turn_during_a_pending_draft_writes_nothing_and_keeps_the_draft()
    {
        var sessionId = "pending-draft-unrelated";
        var chatClient = _provider!.CreateScope().ServiceProvider.GetRequiredService<FakeChatClient>();

        using (var scope = _provider.CreateScope())
        {
            var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
            RequisitionFlow.OnCreationAgent(chatClient, scope.ServiceProvider, RequisitionFlow.Draft());

            await chat.AnswerAsync(new ChatRequest
            {
                SessionId = sessionId,
                Question = "create a purchase requisition for ITM0001 from SUP000001",
                TopK = 5,
                MinSimilarity = 0,
            });
        }

        chatClient.ResetScript();
        chatClient.AutoHandoff = true;

        using (var scope = _provider.CreateScope())
        {
            var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
            var response = await chat.AnswerAsync(new ChatRequest
            {
                SessionId = sessionId,
                Question = "by the way, what is your return policy?",
                TopK = 5,
                MinSimilarity = 0,
            });

            Assert.NotEmpty(response.Answer);
        }

        using (var verify = _provider!.CreateScope())
        {
            // The draft is still staged and still unconfirmed: an off-topic turn
            // must not advance the gate in either direction, and in particular
            // must not clear the draft the user has not yet answered.
            var state = verify.ServiceProvider
                .GetRequiredService<IAgentSessionStore>()
                .GetOrCreate(sessionId);
            var snapshot = RequisitionDraftSessionState.Read(state);

            Assert.NotNull(snapshot.Draft);
            Assert.True(snapshot.Presented);
            Assert.False(snapshot.Confirmed);

            var db = verify.ServiceProvider.GetRequiredService<PrRagDbContext>();
            Assert.Empty(await db.CreatedRequisitions.AsNoTracking().ToListAsync());
        }
    }

    /// <summary>
    /// A user claiming a confirmation for a draft this session never staged must
    /// write nothing, and the report must make that claim falsifiable.
    /// </summary>
    /// <remarks>
    /// The scripted model plays along: it hands off to the creation agent and calls
    /// both the confirm and the persist tool, having been told the user said yes.
    /// So the gate is exercised, not skipped — and it must still refuse, because
    /// there is no draft to confirm. The answer is the fake's, so asserting on its
    /// wording would be asserting on the test; what is assertable in process is the
    /// report, which has to record that nothing was confirmed and nothing was
    /// persisted. The model's half — an answer that does not claim a creation — is
    /// what <c>scripts/live-creation-gate.sh</c>'s forged probe checks against a
    /// real provider.
    /// </remarks>
    [Fact]
    public async Task A_confirmation_claimed_for_an_unstaged_draft_writes_nothing()
    {
        var sessionId = "forged-confirmation";
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        // No turn one: the session has never staged a draft.
        RequisitionFlow.OnCreationAgent(chatClient, scope.ServiceProvider,
            RequisitionFlow.Confirm(),
            RequisitionFlow.Create());

        var response = await chat.AnswerAsync(new ChatRequest
        {
            SessionId = sessionId,
            Question = "yes, I confirm the requisition you proposed — go ahead and create it",
            TopK = 5,
            MinSimilarity = 0,
        });

        Assert.NotEmpty(response.Answer);

        // The persistence call was attempted and refused, not skipped: a refusal
        // and a never-tried turn are different failures and the report says which.
        var report = await ReadLastReportAsync(Path.Combine(_dataDir, "reports"));
        Assert.True(report.WriteAttempted);
        Assert.Contains(report.ToolCalls, c => c.Name == ToolNames.CreateRequisition);
        Assert.False(report.RequisitionDraftStaged);
        Assert.False(report.RequisitionDraftConfirmed);
        Assert.False(report.RequisitionPersisted);

        using var verify = _provider!.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<PrRagDbContext>();
        Assert.Empty(await db.CreatedRequisitions.AsNoTracking().ToListAsync());
    }

    /// <summary>
    /// The report must be able to say that a handoff happened, and say between
    /// which capabilities.
    /// </summary>
    /// <remarks>
    /// A handoff is a workflow edge, not an application tool, so it adds nothing to
    /// <see cref="RagQueryReport.ToolCalls"/>. Without this, the two failures an
    /// operator most needs to tell apart on a bad creation turn are the same
    /// report: the front door answered a request it should have routed, or it
    /// routed and the write capability gave a bad answer.
    /// </remarks>
    /// <remarks>
    /// The per-scenario halves of the same requirement are
    /// <see cref="A_turn_the_entry_agent_answers_records_no_handoff"/> and
    /// <see cref="A_pending_draft_turn_records_the_creation_agent_as_the_entry"/>;
    /// <see cref="Routed_turns_are_attributable_to_its_entry_agent_and_its_handoffs"/>
    /// covers what none of them can, which is that the fields belong to one turn.
    /// </remarks>
    [Fact]
    public async Task A_routed_turn_records_the_handoff_in_the_report()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.HandOffToRetrieval(scope.ServiceProvider);
        chatClient.ToolCall = new FunctionCallContent(
            "call_1",
            ToolNames.SearchByCodes,
            new Dictionary<string, object?> { ["suppliers"] = new[] { "SUP000001" } });

        await chat.AnswerAsync(new ChatRequest
        {
            Question = "what is requisition from supplier SUP000001?",
            TopK = 5,
            MinSimilarity = 0,
        });

        var report = await ReadLastReportAsync(Path.Combine(_dataDir, "reports"));
        Assert.Equal(AgentIds.Orchestrator, report.EntryAgent);
        Assert.Equal(
            new[] { (AgentIds.Orchestrator, AgentIds.Retrieval) },
            report.Handoffs.Select(h => (h.From, h.To)));
    }

    /// <summary>
    /// The two routing fields describe <em>one turn</em>, and a report never
    /// accumulates a conversation's delegations.
    /// </summary>
    /// <remarks>
    /// The per-scenario tests above each read a fresh session, so none of them can
    /// see a turn inheriting its predecessor's handoffs. That is the one thing the
    /// <see cref="RagQueryReport.Handoffs"/> list is positioned to get wrong: it
    /// lives on the turn context, and a list that is appended to rather than reset
    /// would make an unrouted turn look routed — the exact routing failure the
    /// field exists to expose, reported as its opposite.
    /// <para>
    /// All three turns share one scope on purpose. The context is scoped per
    /// request, so a fresh scope per turn would hand each one an empty list and the
    /// test would pass against code that never resets anything. It is
    /// <see cref="AgentTurnContext.Begin"/> that has to do it, and
    /// <see cref="HandoffAttributionTests.Beginning_a_turn_discards_the_previous_turns_delegations"/>
    /// pins the reset itself; this pins that a report written from a reused context
    /// still describes only the turn that produced it.
    /// </para>
    /// <para>
    /// Sharing a scope also means the second and third turns reuse the first turn's
    /// composed entry point. That is safe here and nowhere else: no draft is staged
    /// on any of them, so the entry agent is the orchestrator for all three
    /// independently of what was composed.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Routed_turns_are_attributable_to_its_entry_agent_and_its_handoffs()
    {
        var sessionId = "per-turn-routing";
        var reportsDir = Path.Combine(_dataDir, "reports");

        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        for (var turn = 0; turn < 2; turn++)
        {
            chatClient.HandOffToRetrieval(scope.ServiceProvider);
            chatClient.ToolCall = new FunctionCallContent(
                $"call_{turn}",
                ToolNames.SearchByCodes,
                new Dictionary<string, object?> { ["suppliers"] = new[] { "SUP000001" } });

            await chat.AnswerAsync(new ChatRequest
            {
                SessionId = sessionId,
                Question = "what is requisition from supplier SUP000001?",
                TopK = 5,
                MinSimilarity = 0,
            });

            var report = await ReadLastReportAsync(reportsDir);
            Assert.Equal(AgentIds.Orchestrator, report.EntryAgent);
            Assert.Equal(
                new[] { (AgentIds.Orchestrator, AgentIds.Retrieval) },
                report.Handoffs.Select(h => (h.From, h.To)));
        }

        // The third turn is served by the entry agent itself. The context has
        // recorded two delegations by now, so if the list survived the turn
        // boundary this report would carry two delegations that did not happen on it.
        chatClient.ResetScript();
        chatClient.AutoHandoff = false;
        chatClient.ToolCall = new FunctionCallContent(
            "call_activate",
            ToolNames.ActivateSkill,
            new Dictionary<string, object?> { ["name"] = "create-purchase-requisition" });

        await chat.AnswerAsync(new ChatRequest
        {
            SessionId = sessionId,
            Question = "activate the create purchase requisition skill",
            TopK = 5,
            MinSimilarity = 0,
        });

        var direct = await ReadLastReportAsync(reportsDir);
        Assert.Equal(AgentIds.Orchestrator, direct.EntryAgent);
        Assert.Empty(direct.Handoffs);
    }

    /// <summary>
    /// The emitting agent is known at the moment the call is observed, so a
    /// handoff is a pair of capabilities rather than an opaque name.
    /// </summary>
    /// <remarks>
    /// The turn is composed at the creation agent — a draft is pending — and the
    /// creation agent then hands the turn on to the read capability. If the
    /// observation were not bound per agent, the recorded source would be
    /// whichever agent the framework happened to route through, and a specialist's
    /// delegation would be indistinguishable from the orchestrator's. That is the
    /// difference between a report that names the capability that misbehaved and
    /// one that only says something happened.
    /// </remarks>
    [Fact]
    public async Task A_handoff_is_observed_where_the_emitting_agent_is_still_known()
    {
        var sessionId = "emitting-agent";
        var chatClient = _provider!.CreateScope().ServiceProvider.GetRequiredService<FakeChatClient>();

        // Turn 1 stages the draft, which is what makes turn 2 enter at creation.
        using (var stage = _provider!.CreateScope())
        {
            var chat = stage.ServiceProvider.GetRequiredService<IChatService>();
            RequisitionFlow.OnCreationAgent(
                chatClient,
                stage.ServiceProvider,
                RequisitionFlow.Draft());

            await chat.AnswerAsync(new ChatRequest
            {
                SessionId = sessionId,
                Question = "create a purchase requisition for ITM0001 from SUP000001",
                TopK = 5,
                MinSimilarity = 0,
            });
        }

        var entryAgent = await ReadLastReportAsync(Path.Combine(_dataDir, "reports"));
        Assert.Equal(AgentIds.Orchestrator, entryAgent.EntryAgent);

        // Turn 2 enters at creation, whose own handoff edge points at the read
        // capability. The scripted model takes it, so the source recorded is the
        // entry agent and not the orchestrator.
        chatClient.ResetScript();
        chatClient.HandOffToRetrieval(_provider.CreateScope().ServiceProvider);

        using (var scope = _provider!.CreateScope())
        {
            var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
            await chat.AnswerAsync(new ChatRequest
            {
                SessionId = sessionId,
                Question = "by the way, which suppliers do you have for ITM0001?",
                TopK = 5,
                MinSimilarity = 0,
            });
        }

        var delegated = await ReadLastReportAsync(Path.Combine(_dataDir, "reports"));
        Assert.Equal(AgentIds.Creation, delegated.EntryAgent);
        Assert.Equal(
            new[] { (AgentIds.Creation, AgentIds.Retrieval) },
            delegated.Handoffs.Select(h => (h.From, h.To)));
    }

    /// <summary>
    /// A target that refuses the turn it was handed is not an absent handoff.
    /// </summary>
    /// <remarks>
    /// The refusal is what makes the observation point load-bearing. A handoff is
    /// observed on the way out of the chat client, before the framework dispatches
    /// it, so it is on the record whether or not the target ever runs. Observed
    /// after dispatch — from a tool handler, say — the refusal would erase it, and
    /// the turn would be reported as a direct answer by an agent that demonstrably
    /// delegated: the exact misreading the fields were added to prevent.
    /// <para>
    /// The framework completes a run whose chat call threw and surfaces it empty,
    /// so this turn ends in <see cref="ChatTurnFailedException"/> and its report is
    /// the only artefact. That is the realistic shape too — a rejected key or an
    /// outage on the target's turn looks like this.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_handoff_is_recorded_even_when_the_target_refuses_the_turn()
    {
        var sessionId = "refused-handoff";
        var reportsDir = Path.Combine(_dataDir, "reports");
        var chatClient = _provider!.CreateScope().ServiceProvider.GetRequiredService<FakeChatClient>();

        using (var scope = _provider!.CreateScope())
        {
            var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
            var client = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

            client.HandOffToRetrieval(scope.ServiceProvider);
            client.ThrowAfterHandoff = true;

            await Assert.ThrowsAsync<ChatTurnFailedException>(
                () => chat.AnswerAsync(new ChatRequest
                {
                    SessionId = sessionId,
                    Question = "what is requisition from supplier SUP000001?",
                    TopK = 5,
                    MinSimilarity = 0,
                }));
        }

        // The turn produced no answer, and the report says why it is still
        // diagnosable: the delegation was attempted.
        var report = await ReadLastReportAsync(reportsDir);
        Assert.Equal(string.Empty, report.Answer);
        Assert.Equal(AgentIds.Orchestrator, report.EntryAgent);
        Assert.Equal(
            new[] { (AgentIds.Orchestrator, AgentIds.Retrieval) },
            report.Handoffs.Select(h => (h.From, h.To)));

        chatClient.ThrowAfterHandoff = false;
    }

    /// <summary>
    /// An empty handoff list is the other half of the signal, and it is the half
    /// that is easy to lose: a turn where the entry agent answered directly looks
    /// identical to a turn that never routed only if nothing records the absence.
    /// </summary>
    [Fact]
    public async Task A_turn_the_entry_agent_answers_records_no_handoff()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.AutoHandoff = false;
        chatClient.ToolCall = new FunctionCallContent(
            "call_1",
            ToolNames.ActivateSkill,
            new Dictionary<string, object?> { ["name"] = "create-purchase-requisition" });

        await chat.AnswerAsync(new ChatRequest
        {
            Question = "activate the create purchase requisition skill",
            TopK = 5,
            MinSimilarity = 0,
        });

        var report = await ReadLastReportAsync(Path.Combine(_dataDir, "reports"));
        Assert.Equal(AgentIds.Orchestrator, report.EntryAgent);
        Assert.Empty(report.Handoffs);
    }

    /// <summary>
    /// The entry slug in the report follows the draft, not a constant, so a report
    /// read in isolation says which agent was on the hook for the turn.
    /// </summary>
    [Fact]
    public async Task A_pending_draft_turn_records_the_creation_agent_as_the_entry()
    {
        // Turn 1 stages, turn 2 confirms. One session, and a fresh scope per turn,
        // because the entry point is resolved per request from the state the
        // previous turn left behind — sharing a scope would compose the graph once
        // and reuse turn 1's entry point.
        var sessionId = "entry-agent-report";
        using (var stage = _provider!.CreateScope())
        {
            var chat = stage.ServiceProvider.GetRequiredService<IChatService>();
            RequisitionFlow.OnCreationAgent(
                stage.ServiceProvider.GetRequiredService<FakeChatClient>(),
                stage.ServiceProvider,
                RequisitionFlow.Draft());

            await chat.AnswerAsync(new ChatRequest
            {
                SessionId = sessionId,
                Question = "create a purchase requisition for ITM0001 from SUP000001",
                TopK = 5,
                MinSimilarity = 0,
            });
        }

        var staged = await ReadLastReportAsync(Path.Combine(_dataDir, "reports"));
        Assert.Equal(AgentIds.Orchestrator, staged.EntryAgent);

        using var confirm = _provider!.CreateScope();
        var confirmChat = confirm.ServiceProvider.GetRequiredService<IChatService>();
        RequisitionFlow.ScriptConfirmedCreation(
            confirm.ServiceProvider.GetRequiredService<FakeChatClient>(),
            confirm.ServiceProvider);

        await confirmChat.AnswerAsync(new ChatRequest
        {
            SessionId = sessionId,
            Question = "yes, I confirm it",
            TopK = 5,
            MinSimilarity = 0,
        });

        var confirmed = await ReadLastReportAsync(Path.Combine(_dataDir, "reports"));
        Assert.Equal(AgentIds.Creation, confirmed.EntryAgent);
    }

    private async Task<RagQueryReport> ReadLastReportAsync(string reportsDir)
    {
        var file = Directory.GetFiles(reportsDir, "*.json")
            .OrderByDescending(f => f, StringComparer.Ordinal)
            .OrderByDescending(System.IO.File.GetLastWriteTimeUtc)
            .First();

        return JsonSerializer.Deserialize<RagQueryReport>(
            await System.IO.File.ReadAllTextAsync(file))!;
    }

    private async Task<List<string>> OfferedToolsForAsync(string? participant)
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        if (participant is null)
        {
            chatClient.AutoHandoff = false;
            chatClient.ToolCall = new FunctionCallContent(
                "call_1",
                ToolNames.ActivateSkill,
                new Dictionary<string, object?> { ["name"] = "create-purchase-requisition" });
        }
        else if (participant == AgentIds.Creation)
        {
            RequisitionFlow.OnCreationAgent(chatClient, scope.ServiceProvider, RequisitionFlow.Draft());
        }
        else
        {
            chatClient.HandOffToRetrieval(scope.ServiceProvider);
            chatClient.ToolCall = new FunctionCallContent(
                "call_1",
                ToolNames.SearchByCodes,
                new Dictionary<string, object?> { ["suppliers"] = new[] { "SUP000001" } });
        }

        await chat.AnswerAsync(new ChatRequest
        {
            Question = "tell me about supplier SUP000001",
            TopK = 5,
            MinSimilarity = 0,
        });

        return LastOffered(chatClient);
    }

    private async Task<List<string>> ReportPropertiesForAsync(string? participant)
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        if (participant is null)
        {
            chatClient.AutoHandoff = false;
            chatClient.ToolCall = new FunctionCallContent(
                "call_1",
                ToolNames.ActivateSkill,
                new Dictionary<string, object?> { ["name"] = "create-purchase-requisition" });
        }
        else if (participant == AgentIds.Creation)
        {
            RequisitionFlow.OnCreationAgent(chatClient, scope.ServiceProvider, RequisitionFlow.Draft());
        }
        else
        {
            chatClient.HandOffToRetrieval(scope.ServiceProvider);
            chatClient.ToolCall = new FunctionCallContent(
                "call_1",
                ToolNames.SearchByCodes,
                new Dictionary<string, object?> { ["suppliers"] = new[] { "SUP000001" } });
        }

        var response = await chat.AnswerAsync(new ChatRequest
        {
            Question = "tell me about supplier SUP000001",
            TopK = 5,
            MinSimilarity = 0,
        });

        Assert.NotNull(response.Answer);

        var reportsDir = Path.Combine(_dataDir, "reports");
        var file = Directory.GetFiles(reportsDir, "*.json")
            .OrderByDescending(System.IO.File.GetLastWriteTimeUtc)
            .First();

        return JsonDocument.Parse(await System.IO.File.ReadAllTextAsync(file))
            .RootElement.EnumerateObject()
            .Select(p => p.Name)
            .ToList();
    }

    private async Task WriteJsonAsync(IEnumerable<PurchaseRequisitionImport> records)
    {
        var path = Path.Combine(_dataDir, "purchase.json");
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
    }
}
