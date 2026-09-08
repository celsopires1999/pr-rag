using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace PrRag.Application.Abstractions;

/// <summary>
/// Composes and executes the MAF agent behind a narrow abstraction so the chat
/// orchestrator never constructs or runs an agent itself. The composed agent
/// owns the fixed tool list and session creation; the orchestrator decides WHAT
/// to send each turn and WHERE the response goes.
/// </summary>
public interface IAgentRunService
{
    ValueTask<AgentSession> CreateSessionAsync(CancellationToken cancellationToken = default);

    Task<AgentResponse> RunAsync(
        IList<ChatMessage> messages,
        AgentSession session,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<AgentResponseUpdate> RunStreamingAsync(
        IList<ChatMessage> messages,
        AgentSession session,
        CancellationToken cancellationToken = default);
}