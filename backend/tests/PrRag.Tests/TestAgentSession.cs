using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace PrRag.Tests;

/// <summary>
/// Creates <see cref="AgentSession"/> instances for tests. Sessions are normally
/// produced by an <c>AIAgent</c>, so tests build one from the fake chat client.
/// </summary>
internal static class TestAgentSession
{
    public static ValueTask<AgentSession> NewAsync()
    {
        var agent = new FakeChatClient().AsAIAgent(
            name: "test-agent",
            description: "test agent");
        return agent.CreateSessionAsync();
    }
}