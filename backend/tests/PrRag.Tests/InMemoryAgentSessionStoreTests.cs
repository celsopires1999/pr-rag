using Microsoft.Agents.AI;
using PrRag.Application.Services;
using Xunit;

namespace PrRag.Tests;

public class InMemoryAgentSessionStoreTests
{
    [Fact]
    public async Task RemoveAsync_drops_created_session()
    {
        var store = new InMemoryAgentSessionStore();
        var sessionId = Guid.NewGuid().ToString("N");

        var (_, created) = await store.GetOrCreateAsync(sessionId, TestAgentSession.NewAsync);
        Assert.True(created);

        var removed = await store.RemoveAsync(sessionId);
        Assert.True(removed);

        // A subsequent GetOrCreate with the same id starts a brand-new session.
        var (_, createdAgain) = await store.GetOrCreateAsync(sessionId, TestAgentSession.NewAsync);
        Assert.True(createdAgain);
    }

    [Fact]
    public async Task RemoveAsync_returns_false_for_unknown_id()
    {
        var store = new InMemoryAgentSessionStore();
        var removed = await store.RemoveAsync(Guid.NewGuid().ToString("N"));

        Assert.False(removed);
    }
}