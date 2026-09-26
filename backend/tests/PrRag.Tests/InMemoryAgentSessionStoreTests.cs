using PrRag.Application.Services;
using Xunit;

namespace PrRag.Tests;

public class InMemoryAgentSessionStoreTests
{
    /// <summary>
    /// The store no longer hands out a session, so "the same session" is not the
    /// property under test. What the design needs is the opposite pair: the
    /// per-turn session is always fresh, while the conversation state behind a
    /// session id is stable for the life of the conversation.
    /// </summary>
    [Fact]
    public void GetOrCreate_returns_the_same_state_for_a_session_id()
    {
        var store = new InMemoryAgentSessionStore();
        var sessionId = Guid.NewGuid().ToString("N");

        var first = store.GetOrCreate(sessionId);
        first.History.Add(new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, "hello"));

        var second = store.GetOrCreate(sessionId);

        Assert.Same(first, second);
        Assert.Single(second.History);
    }

    [Fact]
    public async Task BeginTurnAsync_creates_a_fresh_session_every_turn()
    {
        var store = new InMemoryAgentSessionStore();
        var sessionId = Guid.NewGuid().ToString("N");

        var first = await store.BeginTurnAsync(sessionId, TestAgentSession.NewAsync);
        var second = await store.BeginTurnAsync(sessionId, TestAgentSession.NewAsync);

        Assert.NotSame(first, second);
    }

    [Fact]
    public async Task RemoveAsync_drops_the_conversation_state()
    {
        var store = new InMemoryAgentSessionStore();
        var sessionId = Guid.NewGuid().ToString("N");

        var before = store.GetOrCreate(sessionId);
        before.History.Add(new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, "hello"));

        Assert.True(await store.RemoveAsync(sessionId));

        // The conversation is gone, so the next turn starts blank.
        var after = store.GetOrCreate(sessionId);
        Assert.NotSame(before, after);
        Assert.Empty(after.History);
    }

    [Fact]
    public async Task RemoveAsync_returns_false_for_unknown_id()
    {
        var store = new InMemoryAgentSessionStore();
        var removed = await store.RemoveAsync(Guid.NewGuid().ToString("N"));

        Assert.False(removed);
    }
}
