using Microsoft.Extensions.AI;

namespace PrRag.Tests;

public sealed class FakeChatClient : IChatClient
{
    private readonly object _lock = new();
    private string _lastPrompt = string.Empty;

    public string Answer { get; set; } = "fake answer";

    public string LastPrompt
    {
        get { lock (_lock) { return _lastPrompt; } }
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var prompt = string.Join("\n", messages.Select(m => m.Text));
        lock (_lock)
        {
            _lastPrompt = prompt;
        }

        var response = new ChatResponse
        {
            Messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.Assistant, Answer),
            },
        };
        return Task.FromResult(response);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return EmptyAsync();
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> EmptyAsync()
    {
        await Task.CompletedTask;
        yield break;
    }
}
