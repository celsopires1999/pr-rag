using Microsoft.Extensions.AI;
using PrRag.Application.Abstractions;

namespace PrRag.Tests;

public sealed class FakeQueryRewriter : IQueryRewriter
{
    private readonly object _lock = new();
    private int _callCount;
    private string _lastQuestion = string.Empty;
    private IReadOnlyList<ChatMessage>? _lastConversation;

    public int CallCount
    {
        get { lock (_lock) { return _callCount; } }
    }

    public string LastQuestion
    {
        get { lock (_lock) { return _lastQuestion; } }
    }

    public IReadOnlyList<ChatMessage>? LastConversation
    {
        get { lock (_lock) { return _lastConversation; } }
    }

    public Task<string> RewriteAsync(
        string question,
        IReadOnlyList<ChatMessage> conversation,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _callCount++;
            _lastQuestion = question;
            _lastConversation = conversation;
        }

        return Task.FromResult($"optimized: {question}");
    }
}
