using PrRag.Application.Abstractions;

namespace PrRag.Api;

public static class SessionEndpoints
{
    /// <summary>
    /// Discards a stored conversation session so the next turn on the same id
    /// starts fresh. Returns 204 when a session was removed, 404 otherwise.
    /// </summary>
    public static async Task<IResult> Discard(
        string id,
        IAgentSessionStore sessionStore,
        CancellationToken cancellationToken)
    {
        var removed = await sessionStore.RemoveAsync(id, cancellationToken);
        return removed ? Results.NoContent() : Results.NotFound();
    }
}