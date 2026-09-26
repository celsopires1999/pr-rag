using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace PrRag.Application.Services.Agents;

/// <summary>
/// A chat-history provider that contributes nothing, because the application
/// already owns the conversation in <see cref="AgentSessionState"/>.
///
/// <para>
/// Every agent needs one of these. Left to its default, each agent keeps its own
/// history for the life of the session, and with a per-turn session that history
/// is empty — the turn would silently lose the conversation, and any attempt to
/// fix that by replaying history into the run would instead double it, since
/// MAF also feeds the provider's copy. Inerting the provider leaves exactly one
/// owner of the conversation.
/// </para>
/// </summary>
internal sealed class ExternalHistoryProvider : ChatHistoryProvider
{
    protected override ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
        ChatHistoryProvider.InvokingContext context,
        CancellationToken cancellationToken)
        => ValueTask.FromResult<IEnumerable<ChatMessage>>([]);

    protected override ValueTask StoreChatHistoryAsync(
        ChatHistoryProvider.InvokedContext context,
        CancellationToken cancellationToken)
        => ValueTask.CompletedTask;
}
