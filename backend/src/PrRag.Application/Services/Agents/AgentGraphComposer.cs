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
    private const string HandoffInstructions =
        """
        You coordinate purchase-requisition work. Some requests are answered by handing the turn to a specialist that owns that capability: hand off when the request is about finding requisitions, searching for them by meaning, or listing the suppliers for an item. For anything else — creating a requisition, activating a skill, or anything you can already do with your own tools — handle it yourself.

        A specialist talks to the user directly, so you do not need to relay or restate what it says.
        """;

    private readonly ILogger<AgentGraphComposer> _logger;

    public AgentGraphComposer(ILogger<AgentGraphComposer> logger)
    {
        _logger = logger;
    }

    /// <summary>Builds the graph and presents it as one agent.</summary>
    public ComposedAgentGraph Compose(
        IChatClient chatClient,
        ISkillService skillService,
        ISpecialistCatalog catalog)
    {
        var extracted = catalog.Specialists.Where(s => s.IsExtracted).ToList();
        var retained = catalog.Specialists.Where(s => !s.IsExtracted).ToList();

        var participants = new List<AIAgent>();
        var bySlug = new Dictionary<string, ChatClientAgent>(StringComparer.Ordinal);

        foreach (var definition in extracted)
        {
            var specialist = CreateSpecialistAgent(chatClient, definition);

            if (!bySlug.TryAdd(definition.AgentSlug!, specialist))
            {
                throw new InvalidOperationException(
                    $"Two capabilities claim the agent slug '{definition.AgentSlug}'. " +
                    "Agent slugs are routing and telemetry identity, so they cannot be shared.");
            }

            participants.Add(specialist);
        }

        var orchestrator = CreateOrchestratorAgent(chatClient, skillService, retained);
        var workflow = BuildWorkflow(orchestrator, participants);

        _logger.LogInformation(
            "Composed agent graph: orchestrator {OrchestratorId} retaining {RetainedCount} capabilities, {ParticipantCount} specialist(s) {Participants}.",
            AgentIds.Orchestrator,
            retained.Count,
            participants.Count,
            string.Join(", ", bySlug.Keys));

        var agent = workflow.AsAIAgent(
            id: AgentIds.Orchestrator,
            name: AgentInstructions.AgentName,
            description: AgentInstructions.AgentDescription,
            includeExceptionDetails: false,
            includeWorkflowOutputsInResponse: false);

        return new ComposedAgentGraph(agent, orchestrator, bySlug, workflow);
    }

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

    private static Workflow BuildWorkflow(ChatClientAgent orchestrator, List<AIAgent> participants)
    {
        var builder = new HandoffWorkflowBuilder(orchestrator);

        if (participants.Count == 0)
        {
            // No handoff edges to declare, but the graph is still built so the
            // caller-facing type is identical whether or not anything has been
            // extracted yet.
            return builder.Build();
        }

        builder.AddParticipants(participants);
        builder.WithHandoffInstructions(HandoffInstructions);

        foreach (var participant in participants)
        {
            builder.WithHandoff(orchestrator, participant, $"Hand the turn to {participant.Name} for this request.");
        }

        // Note: EnableReturnToPrevious is deliberately NOT used. Despite the
        // name it means "route subsequent user turns straight back to the
        // specialist that handled the last turn", i.e. sticky routing that
        // bypasses the orchestrator — the opposite of what this graph wants.
        // Leaving it off keeps every turn entering at the orchestrator, which is
        // what makes the per-agent turn context and the injected skill guidance
        // reachable on every turn.
        return builder
            .EmitAgentResponseEvents(true)
            .EmitAgentResponseUpdateEvents(true)
            .Build();
    }
}
