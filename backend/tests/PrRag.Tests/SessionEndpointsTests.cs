using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PrRag.Api;
using PrRag.Application.Services;
using Xunit;

namespace PrRag.Tests;

public class SessionEndpointsTests
{
    [Fact]
    public async Task Discard_known_session_returns_204_and_removes_it()
    {
        var store = new InMemoryAgentSessionStore();
        var sessionId = Guid.NewGuid().ToString("N");
        await store.GetOrCreateAsync(sessionId, TestAgentSession.NewAsync);

        var result = await SessionEndpoints.Discard(sessionId, store, CancellationToken.None);

        var context = NewHttpContext();
        await result.ExecuteAsync(context);
        Assert.Equal(204, context.Response.StatusCode);

        // The stored session is gone; the next look-up starts fresh.
        var (_, created) = await store.GetOrCreateAsync(sessionId, TestAgentSession.NewAsync);
        Assert.True(created);
    }

    [Fact]
    public async Task Discard_unknown_session_returns_404()
    {
        var store = new InMemoryAgentSessionStore();

        var result = await SessionEndpoints.Discard(Guid.NewGuid().ToString("N"), store, CancellationToken.None);

        var context = NewHttpContext();
        await result.ExecuteAsync(context);
        Assert.Equal(404, context.Response.StatusCode);
    }

    private static DefaultHttpContext NewHttpContext()
    {
        return new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
    }
}