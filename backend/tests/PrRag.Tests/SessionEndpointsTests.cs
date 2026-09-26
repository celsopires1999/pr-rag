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
        var before = store.GetOrCreate(sessionId);

        var result = await SessionEndpoints.Discard(sessionId, store, CancellationToken.None);

        var context = NewHttpContext();
        await result.ExecuteAsync(context);
        Assert.Equal(204, context.Response.StatusCode);

        // The conversation is gone; the next turn starts from a blank state.
        Assert.NotSame(before, store.GetOrCreate(sessionId));
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