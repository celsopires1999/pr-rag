using Microsoft.Agents.AI;

namespace PrRag.Application.Abstractions;

public interface IAgentSessionStore
{
    /// <summary>
    /// Returns the <see cref="AgentSession"/> for <paramref name="sessionId"/>,
    /// creating one via <paramref name="create"/> when it does not exist yet.
    /// </summary>
    Task<(AgentSession Session, bool Created)> GetOrCreateAsync(
        string sessionId,
        Func<ValueTask<AgentSession>> create,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(string sessionId, CancellationToken cancellationToken = default);
}