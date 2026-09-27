using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PrRag.Application.Abstractions;
using PrRag.Application.Services.Agents.Specialists;

namespace PrRag.Application.Services.Agents;

/// <summary>
/// Runs the composed agent behind a narrow abstraction so the chat orchestrator
/// never constructs or runs an agent itself.
///
/// <para>
/// The agent here is the workflow-backed <see cref="AIAgent"/> produced by
/// <see cref="AgentGraphComposer"/>, not a <c>ChatClientAgent</c>. That is the
/// whole reason the seam exists: <c>AsAIAgent(Workflow, ...)</c> returns an
/// agent, so adopting a handoff graph changed the composition and this class,
/// and left <see cref="IAgentRunService"/>, <c>ChatService</c>, the endpoints,
/// the streaming path, and the session store untouched.
/// </para>
///
/// <para>
/// Composition is deferred to the first use and happens exactly once per request,
/// not once per construction. The entry point depends on the session state
/// (a staged draft sends the turn to the creation agent), and the state arrives
/// on <see cref="AgentTurnContext"/> during the turn, after this object is built.
/// Composing in the constructor would therefore read a null state and always
/// build the orchestrator-entry graph — a defect that would look correct on
/// every turn except the one the write path depends on.
/// </para>
/// </summary>
public sealed class AgentRunService : IAgentRunService
{
    private readonly AgentGraphComposer _composer;
    private readonly IChatClient _chatClient;
    private readonly ISkillService _skillService;
    private readonly ISpecialistCatalog _catalog;
    private readonly AgentTurnContext _turnContext;
    private ComposedAgentGraph? _graph;

    public AgentRunService(
        AgentGraphComposer composer,
        IChatClient chatClient,
        ISkillService skillService,
        ISpecialistCatalog catalog,
        AgentTurnContext turnContext)
    {
        _composer = composer;
        _chatClient = chatClient;
        _skillService = skillService;
        _catalog = catalog;
        _turnContext = turnContext;
    }

    /// <summary>
    /// The composed graph for this request, composed on first access so the entry
    /// point can be resolved from the turn's state. For per-agent assertions and
    /// routing diagnostics.
    /// </summary>
    public ComposedAgentGraph Graph => _graph ??= _composer.Compose(
        _chatClient,
        _skillService,
        _catalog,
        AgentGraphComposer.ResolveEntryPoint(_turnContext.State));

    public ValueTask<AgentSession> CreateSessionAsync(CancellationToken cancellationToken = default)
        => Graph.Agent.CreateSessionAsync(cancellationToken);

    public Task<AgentResponse> RunAsync(
        IList<ChatMessage> messages,
        AgentSession session,
        CancellationToken cancellationToken = default)
        => Graph.Agent.RunAsync(messages, session, cancellationToken: cancellationToken);

    public IAsyncEnumerable<AgentResponseUpdate> RunStreamingAsync(
        IList<ChatMessage> messages,
        AgentSession session,
        CancellationToken cancellationToken = default)
        => Graph.Agent.RunStreamingAsync(messages, session, cancellationToken: cancellationToken);
}
