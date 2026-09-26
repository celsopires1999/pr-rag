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
/// </summary>
public sealed class AgentRunService : IAgentRunService
{
    private readonly ComposedAgentGraph _graph;

    public AgentRunService(AgentGraphComposer composer, IChatClient chatClient, ISkillService skillService, ISpecialistCatalog catalog)
    {
        _graph = composer.Compose(chatClient, skillService, catalog);
    }

    /// <summary>The composed graph, for the per-agent assertions and routing diagnostics.</summary>
    public ComposedAgentGraph Graph => _graph;

    public ValueTask<AgentSession> CreateSessionAsync(CancellationToken cancellationToken = default)
        => _graph.Agent.CreateSessionAsync(cancellationToken);

    public Task<AgentResponse> RunAsync(
        IList<ChatMessage> messages,
        AgentSession session,
        CancellationToken cancellationToken = default)
        => _graph.Agent.RunAsync(messages, session, cancellationToken: cancellationToken);

    public IAsyncEnumerable<AgentResponseUpdate> RunStreamingAsync(
        IList<ChatMessage> messages,
        AgentSession session,
        CancellationToken cancellationToken = default)
        => _graph.Agent.RunStreamingAsync(messages, session, cancellationToken: cancellationToken);
}
