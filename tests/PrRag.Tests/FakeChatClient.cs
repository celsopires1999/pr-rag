using Microsoft.Extensions.AI;

namespace PrRag.Tests;

public sealed class FakeChatClient : IChatClient
{
    private readonly object _lock = new();
    private readonly List<ChatMessage> _messages = new();
    private string _lastPrompt = string.Empty;
    private int _calls;

    public string Answer { get; set; } = "fake answer";

    /// <summary>
    /// When set, the chat model is scripted to issue this tool call on its first
    /// turn (exactly once), then answer with <see cref="Answer"/> on a subsequent turn.
    /// </summary>
    public FunctionCallContent? ToolCall { get; set; }

    private int _toolCallConsumed;

    public string LastPrompt
    {
        get { lock (_lock) { return _lastPrompt; } }
    }

    public int CallCount
    {
        get { lock (_lock) { return _calls; } }
    }

    public IReadOnlyList<ChatMessage> LastMessages
    {
        get { lock (_lock) { return _messages.ToList(); } }
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = messages.ToList();
        var prompt = string.Join("\n", snapshot.Select(m => m.Text));
        lock (_lock)
        {
            _calls++;
            _lastPrompt = prompt;
            _messages.Clear();
            _messages.AddRange(snapshot.Select(m => m));
        }

        ChatMessage reply;
        if (ToolCall is { } call && Interlocked.CompareExchange(ref _toolCallConsumed, 1, 0) == 0)
        {
            reply = new ChatMessage(ChatRole.Assistant, new List<AIContent> { call });
        }
        else
        {
            reply = new ChatMessage(ChatRole.Assistant, Answer);
        }

        return Task.FromResult(new ChatResponse { Messages = new List<ChatMessage> { reply } });
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
