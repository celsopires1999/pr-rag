namespace PrRag.Application.Abstractions;

public interface IQueryRewriter
{
    Task<string> RewriteAsync(
        string question,
        CancellationToken cancellationToken = default);
}
