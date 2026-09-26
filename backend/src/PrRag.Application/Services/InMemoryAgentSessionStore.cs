using System.Collections.Concurrent;
using Microsoft.Agents.AI;
using PrRag.Application.Abstractions;
using PrRag.Application.Services.Agents;

namespace PrRag.Application.Services;

/// <summary>
/// In-memory, per-process store of conversation state keyed by a client-supplied
/// session identifier. Lost on restart.
/// </summary>
public sealed class InMemoryAgentSessionStore : IAgentSessionStore
{
    private readonly ConcurrentDictionary<string, AgentSessionState> _states = new(StringComparer.Ordinal);

    public AgentSessionState GetOrCreate(string sessionId)
        => _states.GetOrAdd(sessionId, static _ => new AgentSessionState());

    public ValueTask<AgentSession> BeginTurnAsync(
        string sessionId,
        Func<ValueTask<AgentSession>> create,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GetOrCreate(sessionId);
        return create();
    }

    public Task<bool> RemoveAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_states.TryRemove(sessionId, out _));
    }
}
