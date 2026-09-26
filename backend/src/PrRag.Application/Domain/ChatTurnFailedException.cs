namespace PrRag.Application.Domain;

/// <summary>
/// A turn that produced no answer did not succeed, and must not be reported as
/// one.
/// </summary>
///
/// <para>
/// The agent framework completes a run normally when the underlying chat client
/// throws — an expired or rejected API key, a provider outage, a refused request —
/// and surfaces the failure as an empty run rather than as an exception. The
/// framework's own log carries the real cause, but the caller was meanwhile given
/// <c>200 OK</c> with an empty answer, and the report for the turn recorded a
/// no-context fallback that was never written.
/// </para>
///
/// <para>
/// That combination is the worst shape a failure can take: nothing in the response
/// or the report says anything went wrong, so the only evidence is a log line
/// nobody reads, and an outage is indistinguishable from a quiet turn. This
/// exception restores the signal — a turn yields an answer or it fails.
/// </para>
/// </remarks>
public sealed class ChatTurnFailedException : Exception
{
    public ChatTurnFailedException(string message)
        : base(message)
    {
    }

    public ChatTurnFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
