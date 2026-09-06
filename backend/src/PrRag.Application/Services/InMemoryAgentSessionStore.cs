using System.Collections.Concurrent;
using Microsoft.Agents.AI;
using PrRag.Application.Abstractions;

namespace PrRag.Application.Services;

/// <summary>
/// In-memory, per-process store of agent sessions keyed by a client-supplied
/// session identifier. Sessions are lost on restart.
/// </summary>
public sealed class InMemoryAgentSessionStore : IAgentSessionStore
{
    private readonly ConcurrentDictionary<string, Lazy<ValueTask<AgentSession>>> _sessions = new();

    public async Task<(AgentSession Session, bool Created)> GetOrCreateAsync(
        string sessionId,
        Func<ValueTask<AgentSession>> create,
        CancellationToken cancellationToken = default)
    {
        var newLazy = new Lazy<ValueTask<AgentSession>>(create, LazyThreadSafetyMode.ExecutionAndPublication);
        var lazy = _sessions.GetOrAdd(sessionId, newLazy);
        var created = ReferenceEquals(lazy, newLazy);

        try
        {
            var session = await lazy.Value.AsTask().WaitAsync(cancellationToken);
            return (session, created);
        }
        catch
        {
            // A failed factory must not poison the store for later retries.
            _sessions.TryRemove(new KeyValuePair<string, Lazy<ValueTask<AgentSession>>>(sessionId, lazy));
            throw;
        }
    }

    public Task<bool> RemoveAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_sessions.TryRemove(sessionId, out _));
    }
}