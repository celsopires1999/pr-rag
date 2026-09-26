using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PrRag.Application.Abstractions;
using PrRag.Application.Services.Agents.Specialists;

namespace PrRag.Application.Services.Agents;

/// <summary>
/// Composes the <c>ChatClientAgent</c> from the registered <c>IChatClient</c>,
/// the <see cref="AgentInstructions"/> identity, the <see cref="ISpecialistCatalog"/>
/// tool list, and the current skill manifest, then executes runs. Mirror of the
/// AgentLab pattern: composition (agent + instructions + tools) is kept apart
/// from per-turn orchestration.
///
/// The prompt is compiled here, on the agent's <c>instructions</c>, rather than
/// being handed in as a message by the caller. Two reasons: the agent injects
/// instructions on every run, so a skill manifest reloaded mid-session reaches
/// the next turn instead of freezing at session creation; and it keeps prompt
/// assembly out of the turn orchestrator entirely.
/// </summary>
public sealed class AgentRunService : IAgentRunService
{
    private readonly ChatClientAgent _agent;
    private readonly ChatClientAgentRunOptions _runOptions;

    public AgentRunService(
        IChatClient chatClient,
        ISkillService skillService,
        ISpecialistCatalog catalog)
    {
        _agent = chatClient.AsAIAgent(
            name: AgentInstructions.AgentName,
            description: AgentInstructions.AgentDescription,
            instructions: AgentInstructions.ComposeSystemPrompt(
                skillService.GetManifest(),
                catalog.ActionBlocks));

        _runOptions = new ChatClientAgentRunOptions(new ChatOptions
        {
            Tools = catalog.AllTools,
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
