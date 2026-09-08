using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PrRag.Application.Abstractions;

namespace PrRag.Application.Services.Agents;

/// <summary>
/// Composes the <c>ChatClientAgent</c> from the registered <c>IChatClient</c>,
/// the <see cref="AgentInstructions"/> identity, and the
/// <see cref="PurchaseRequisitionTools"/> tool list, then executes runs. Mirror
/// of the AgentLab pattern: composition (agent + instructions + tools) is kept
/// apart from per-turn orchestration.
/// </summary>
public sealed class AgentRunService : IAgentRunService
{
    private readonly ChatClientAgent _agent;
    private readonly ChatClientAgentRunOptions _runOptions;

    public AgentRunService(IChatClient chatClient, PurchaseRequisitionTools tools)
    {
        _agent = chatClient.AsAIAgent(
            name: AgentInstructions.AgentName,
            description: AgentInstructions.AgentDescription);

        _runOptions = new ChatClientAgentRunOptions(new ChatOptions
        {
            Tools = tools.All,
            ToolMode = ChatToolMode.Auto,
        });
    }

    public ValueTask<AgentSession> CreateSessionAsync(CancellationToken cancellationToken = default)
        => _agent.CreateSessionAsync(cancellationToken);

    public Task<AgentResponse> RunAsync(
        IList<ChatMessage> messages,
        AgentSession session,
        CancellationToken cancellationToken = default)
        => _agent.RunAsync(messages, session, _runOptions, cancellationToken);

    public IAsyncEnumerable<AgentResponseUpdate> RunStreamingAsync(
        IList<ChatMessage> messages,
        AgentSession session,
        CancellationToken cancellationToken = default)
        => _agent.RunStreamingAsync(messages, session, _runOptions, cancellationToken);
}