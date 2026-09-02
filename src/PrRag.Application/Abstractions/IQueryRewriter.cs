using Microsoft.Extensions.AI;

namespace PrRag.Application.Abstractions;

public interface IQueryRewriter
{
    Task<string> RewriteAsync(
        string question,
        IReadOnlyList<ChatMessage> conversation,
        CancellationToken cancellationToken = default);
}
