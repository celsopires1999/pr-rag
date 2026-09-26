using PrRag.Application.Services.Agents;

namespace PrRag.Application.Abstractions;

/// <summary>
/// Per-client-session conversation state, plus a factory for the per-turn MAF
/// session that runs against it.
///
/// <para>
/// The store deliberately does <b>not</b> hand out a long-lived
/// <c>AgentSession</c>. MAF binds a session to the agent instance that created
/// it, so a cached session pins one composition of the agent graph for the life
/// of the conversation — and the graph's tools hold request-scoped services, so
/// that composition cannot outlive the request that built it. Instead each turn
/// gets a fresh session and the conversation itself is carried by
/// <see cref="AgentSessionState"/>.
/// </para>
/// </summary>
public interface IAgentSessionStore
{
    /// <summary>
    /// The state for <paramref name="sessionId"/>, created empty on first use.
    /// Stable for the life of the conversation, unlike the per-turn session.
    /// </summary>
    AgentSessionState GetOrCreate(string sessionId);

    /// <summary>
    /// Creates the session for one turn via <paramref name="create"/>. Always
    /// fresh: nothing about the session survives the turn, because everything
    /// that has to survive lives in <see cref="AgentSessionState"/>.
    /// </summary>
    ValueTask<Microsoft.Agents.AI.AgentSession> BeginTurnAsync(
        string sessionId,
        Func<ValueTask<Microsoft.Agents.AI.AgentSession>> create,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(string sessionId, CancellationToken cancellationToken = default);
}
