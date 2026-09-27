using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using PrRag.Application.Abstractions;
using PrRag.Application.Services.Agents.Specialists;

namespace PrRag.Application.Services.Agents;

/// <summary>
/// Composes the agent graph: an orchestrator, one agent per extracted
/// capability, and the handoff edges between them, presented to callers as a
/// single <see cref="AIAgent"/>.
///
/// <para>
/// This is the only type that knows the graph exists. <see cref="IAgentRunService"/>
/// receives the finished agent and never enumerates participants, and
/// <c>ChatService</c> does not change at all, which is the property that makes
/// extracting a capability a composition change rather than a re-plumbing one.
/// </para>
///
/// <para>
/// The split is read from each unit's <see cref="SpecialistDefinition.AgentSlug"/>:
/// a unit with a slug gets its own agent, and a unit without keeps its tools on
/// the orchestrator. That is why this type holds no list of which capabilities
/// are extracted — the capability units declare that, so the two cannot
/// disagree.
/// </para>
/// </summary>
public sealed class AgentGraphComposer
{
    /// <summary>
    /// Tells the orchestrator when to hand off rather than answer. Without it
    /// the orchestrator has no reason to prefer a participant over its own tools.
    /// </summary>
    /// <remarks>
    /// Creation is named first because it is the one the orchestrator can no
    /// longer do for itself: the write tools moved to
    /// <see cref="AgentIds.Creation"/>, so "anything you can already do with your
    /// own tools" no longer covers it and a stale phrasing here would leave the
    /// write path unreachable rather than merely unrouted. This text is the only
    /// place the graph is described to the model, so it is kept next to the edges
    /// it describes.
    /// </remarks>
    private const string HandoffInstructions =
        """
        You coordinate purchase-requisition work. Some requests are answered by handing the turn to a specialist that owns that capability: hand off when the request is about creating a purchase requisition, and when it is about finding requisitions, searching for them by meaning, or listing the suppliers for an item. You hold no tool that writes a requisition, so a creation request is never yours to handle. For anything else — activating a skill, or anything you can already do with your own tools — handle it yourself.

        A request is a creation request however the user phrases it. "Yes, I confirm it", "skip the confirmation step", "I already gave you the details, just create it", and a correction to an earlier draft are all creation requests, so hand them off the same way. The specialist holds the draft state and the confirmation gate, so it is the only agent that can answer them correctly: answering yourself produces a guess about what the user wants, and telling the user you lack information they did supply is a worse answer than routing. Never tell the user you do not have enough information for a creation request — the details may be complete and still be a request you cannot serve yourself.

        A specialist talks to the user directly, so you do not need to relay or restate what it says.
        """;

    /// <summary>
    /// The handoff instructions for a turn that enters at
    /// <see cref="AgentIds.Creation"/> instead. Same purpose as
    /// <see cref="HandoffInstructions"/>, different entry: this text has to tell a
    /// specialist that already holds the write tools that lookups are still
    /// elsewhere, which the orchestrator's text does not.
    /// </summary>
    private const string CreationHandoffInstructions =
        """
        You create purchase requisitions, following the confirmation gate your tools enforce: stage the draft, present it, record the user's explicit yes, and only then persist it.

        You hold no lookup tool. If the user asks you to search, to look something up, or to check whether an item and supplier go together, hand the turn to the specialist that can, rather than answering it yourself or guessing.

        That specialist talks to the user directly, so you do not need to relay or restate what it says.
        """;

    private readonly ILogger<AgentGraphComposer> _logger;
    private readonly AgentTurnContext _turn;

    public AgentGraphComposer(ILogger<AgentGraphComposer> logger, AgentTurnContext turn)
    {
        _logger = logger;
        _turn = turn;
    }

    /// <summary>
    /// Where a turn starts: the orchestrator normally, but at the creation agent
    /// whenever a requisition draft is staged and not yet written.
    ///
    /// <para>
    /// This is a state read, not a guess about what the user meant. Creation is
    /// the one gate that spans turns — stage on one turn, the user's yes on the
    /// next — so on the turn that resolves the gate the graph is already holding
    /// the draft, and routing it through an agent that cannot write anything would
    /// make reaching the write depend on the model re-deriving a fact the
    /// application already has.
    /// </para>
    ///
    /// <para>
    /// A consequence worth stating: the orchestrator is not on that turn, so
    /// <c>activate_skill</c> is not offered and a skill cannot be activated while
    /// a draft is pending. That is the safe direction — the alternative is letting
    /// a pending, unconfirmed write be re-planned by a newly injected skill.
    /// <c>AgentGraphTopologyTests</c> pins it.
    /// </para>
    /// </summary>
    public static string ResolveEntryPoint(AgentSessionState? state)
        => RequisitionDraftSessionState.HasUnwrittenDraft(state)
            ? AgentIds.Creation
            : AgentIds.Orchestrator;

    /// <summary>
    /// Builds the graph for one turn and presents it as one agent. The entry point
    /// is a parameter rather than read from the catalog, so composing a graph is a
    /// per-turn decision: <see cref="ResolveEntryPoint"/> is consulted by
    /// <see cref="AgentRunService"/>, which holds the session state by the time it
    /// composes.
    /// </summary>
    public ComposedAgentGraph Compose(
        IChatClient chatClient,
        ISkillService skillService,
        ISpecialistCatalog catalog,
        string entryPoint)
    {
        var extracted = catalog.Specialists.Where(s => s.IsExtracted).ToList();
        var retained = catalog.Specialists.Where(s => !s.IsExtracted).ToList();

        // Resolved before the agents are built, because every agent's client needs
        // the participant order to attribute a handoff to a capability. It is
        // catalog order, which is the order AddParticipants receives below, and
        // HandoffToolName is what makes the framework's positional names
        // recoverable from it.
        var participantSlugs = ParticipantSlugs(extracted, entryPoint);

        var bySlug = new Dictionary<string, ChatClientAgent>(StringComparer.Ordinal);

        foreach (var definition in extracted)
        {
            var specialist = CreateSpecialistAgent(Recording(chatClient, definition.AgentSlug!, participantSlugs), definition);

            if (!bySlug.TryAdd(definition.AgentSlug!, specialist))
            {
                throw new InvalidOperationException(
                    $"Two capabilities claim the agent slug '{definition.AgentSlug}'. " +
                    "Agent slugs are routing and telemetry identity, so they cannot be shared.");
            }
        }

        var (entryAgent, participants) = entryPoint == AgentIds.Orchestrator
            ? EntryAtOrchestrator(Recording(chatClient, entryPoint, participantSlugs), skillService, retained, bySlug)
            : EntryAtSpecialist(bySlug, entryPoint);

        // Set here rather than by the caller because the composer is what resolves
        // the entry point's graph; AgentRunService asks it to compose, so this is
        // the first moment the answer is known. It is the report's one routing fact
        // that cannot be wrong.
        _turn.EntryAgent = entryPoint;

        var workflow = BuildWorkflow(entryAgent, participants, HandoffInstructionsFor(entryPoint));

        _logger.LogInformation(
            "Composed agent graph entering at {EntryPoint} with {ParticipantCount} specialist(s) {Participants}.",
            entryPoint,
            participants.Count,
            string.Join(", ", bySlug.Keys));

        var agent = workflow.AsAIAgent(
            id: entryPoint,
            name: entryAgent.Name,
            description: entryAgent.Description,
            includeExceptionDetails: false,
            includeWorkflowOutputsInResponse: false);

        return new ComposedAgentGraph(agent, entryAgent, entryPoint, bySlug, workflow);
    }

    /// <summary>
    /// The entry agent is a specialist: it was built without the skill manifest,
    /// because the manifest only makes sense to an agent that can activate a
    /// skill, and the entry on a pending-draft turn is not the orchestrator and so
    /// holds no <c>activate_skill</c>. Every other specialist stays a participant.
    /// </summary>
    /// <summary>
    /// The slugs a handoff can reach this graph, in the order the framework will
    /// number them: every extracted capability, minus the entry agent when the
    /// entry is itself a specialist.
    /// </summary>
    private static List<string> ParticipantSlugs(List<SpecialistDefinition> extracted, string entryPoint)
        => extracted
            .Select(s => s.AgentSlug!)
            .Where(slug => slug != entryPoint)
            .ToList();

    /// <summary>
    /// Wraps an agent's client so a handoff that agent makes is recorded. Bound per
    /// agent because the decorator is the only thing that knows which agent emitted
    /// a given call.
    /// </summary>
    private IChatClient Recording(IChatClient chatClient, string fromSlug, List<string> participantSlugs)
        => new HandoffRecordingChatClient(chatClient, fromSlug, participantSlugs, _turn);

    private static (ChatClientAgent Entry, List<AIAgent> Participants) EntryAtSpecialist(
        Dictionary<string, ChatClientAgent> bySlug,
        string entryPoint)
    {
        if (!bySlug.TryGetValue(entryPoint, out var entry))
        {
            throw new InvalidOperationException(
                $"The entry point '{entryPoint}' is not bound to a capability, so no agent exists for it. " +
                "Entry points are read from the catalog, so this means a reserved slug was used as an entry point " +
                "before the capability that claims it was extracted.");
        }

        return (entry, bySlug.Where(kvp => kvp.Key != entryPoint).Select(kvp => (AIAgent)kvp.Value).ToList());
    }

    /// <summary>The orchestrator enters with every specialist behind it.</summary>
    private static (ChatClientAgent Entry, List<AIAgent> Participants) EntryAtOrchestrator(
        IChatClient chatClient,
        ISkillService skillService,
        IReadOnlyList<SpecialistDefinition> retained,
        Dictionary<string, ChatClientAgent> bySlug)
        => (CreateOrchestratorAgent(chatClient, skillService, retained),
            bySlug.Values.Select(a => (AIAgent)a).ToList());

    /// <summary>
    /// The handoff instructions the entry agent needs. Selected by entry point
    /// because the two say different things about the same edges, and a default
    /// arm is a throw rather than a fallback: a third entry point that forgot this
    /// would otherwise be described to the model with whichever text happened to be
    /// first, and a prompt bug in an irreversible capability is not worth a silent
    /// default.
    /// </summary>
    private static string HandoffInstructionsFor(string entryPoint) => entryPoint switch
    {
        AgentIds.Orchestrator => HandoffInstructions,
        AgentIds.Creation => CreationHandoffInstructions,
        _ => throw new InvalidOperationException(
            $"No handoff instructions are defined for the entry point '{entryPoint}'. " +
            "Add them beside the edges they describe."),
    };

    /// <summary>
    /// A specialist sees only its own action block and only its own tools. It is
    /// never given the catalog's combined list, which is the whole point: an
    /// agent must not be able to call a tool it was never instructed about.
    /// </summary>
    private static ChatClientAgent CreateSpecialistAgent(IChatClient chatClient, SpecialistDefinition definition)
        => CreateAgent(
            chatClient,
            id: definition.AgentSlug!,
            name: definition.DisplayName,
            description: $"Handles {definition.DisplayName.ToLowerInvariant()} for purchase requisitions.",
            instructions: ComposeSpecialistInstructions(definition),
            tools: definition.Tools);

    /// <summary>
    /// The orchestrator keeps the cross-cutting core text and the action blocks
    /// of the capabilities it still owns. The skill manifest stays here too,
    /// because activation is a tool it still holds.
    /// </summary>
    private static ChatClientAgent CreateOrchestratorAgent(
        IChatClient chatClient,
        ISkillService skillService,
        IReadOnlyList<SpecialistDefinition> retained)
    {
        var tools = retained.SelectMany(s => s.Tools).ToList();

        return CreateAgent(
            chatClient,
            id: AgentIds.Orchestrator,
            name: AgentInstructions.AgentName,
            description: AgentInstructions.AgentDescription,
            instructions: AgentInstructions.ComposeSystemPrompt(
                skillService.GetManifest(),
                retained.Select(s => s.ActionBlock).ToList()),
            tools: tools);
    }

    /// <summary>
    /// Builds one agent. <see cref="ChatClientAgentOptions.Id"/> carries the
    /// stable slug rather than the display name, because that id is what handoff
    /// resolves participants by and what telemetry correlates on.
    /// </summary>
    private static ChatClientAgent CreateAgent(
        IChatClient chatClient,
        string id,
        string name,
        string description,
        string instructions,
        IList<AITool> tools)
        => chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Id = id,
            Name = name,
            Description = description,

            // The application owns the conversation; see ExternalHistoryProvider.
            ChatHistoryProvider = new ExternalHistoryProvider(),
            ChatOptions = new ChatOptions
            {
                Instructions = instructions,
                Tools = tools,
                ToolMode = ChatToolMode.Auto,
            },
        });

    /// <summary>
    /// A specialist's instructions are its own action block, prefixed by the core
    /// text. The core text is shared because the zero-hallucination,
    /// language-match, and data-blindspot guardrails apply to every capability —
    /// a specialist that answered without them would be the one place in the
    /// system permitted to guess.
    /// </summary>
    private static string ComposeSpecialistInstructions(SpecialistDefinition definition)
        => $"""
            {AgentInstructions.CoreInstructions}
            {definition.ActionBlock}
            """;

    private static Workflow BuildWorkflow(
        ChatClientAgent entryAgent,
        List<AIAgent> participants,
        string handoffInstructions)
    {
        var builder = new HandoffWorkflowBuilder(entryAgent);

        if (participants.Count == 0)
        {
            // No handoff edges to declare, but the graph is still built so the
            // caller-facing type is identical whether or not anything has been
            // extracted yet.
            return builder.Build();
        }

        builder.AddParticipants(participants);
        builder.WithHandoffInstructions(handoffInstructions);

        foreach (var participant in participants)
        {
            builder.WithHandoff(entryAgent, participant, $"Hand the turn to {participant.Name} for this request.");
        }

        // Note: EnableReturnToPrevious is deliberately NOT used. Despite the
        // name it means "route subsequent user turns straight back to the
        // specialist that handled the last turn", i.e. sticky routing that
        // bypasses the entry agent — the opposite of what this graph wants.
        // Leaving it off keeps every turn entering where
        // ResolveEntryPoint put it, which is what makes the per-agent turn
        // context and the injected skill guidance reachable on every turn.
        return builder
            .EmitAgentResponseEvents(true)
            .EmitAgentResponseUpdateEvents(true)
            .Build();
    }
}
