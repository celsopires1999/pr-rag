using PrRag.Application.Abstractions;

namespace PrRag.Tests;

public sealed class FakeQueryRewriter : IQueryRewriter
{
    private readonly object _lock = new();
    private int _callCount;
    private string _lastQuestion = string.Empty;

    public int CallCount
    {
        get { lock (_lock) { return _callCount; } }
    }

    public string LastQuestion
    {
        get { lock (_lock) { return _lastQuestion; } }
    }

    public Task<string> RewriteAsync(string question, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _callCount++;
            _lastQuestion = question;
        }

        return Task.FromResult($"optimized: {question}");
    }
}
