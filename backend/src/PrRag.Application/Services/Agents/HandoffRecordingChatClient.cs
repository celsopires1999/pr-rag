using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using PrRag.Application.DTOs;

namespace PrRag.Application.Services.Agents;

/// <summary>
/// Records the handoffs a turn made, by watching the function calls each agent's
/// model emits.
/// </summary>
/// <remarks>
/// <para>
/// A handoff is not a tool the application registers, so nothing else can see one.
/// It arrives as a <see cref="FunctionCallContent"/> in the model's own response —
/// the framework turns it into a graph edge afterwards — which means the only
/// place it is observable is on the way out of the chat client, before the
/// function-invocation layer dispatches it. Hence a decorator rather than a tool
/// handler: a handler would run after the handoff had already been taken and
/// could not attribute it to the agent that made it.
/// </para>
/// <para>
/// The agent is known because the composer binds this decorator per agent, so a
/// handoff is recorded as a from/to pair of capability slugs rather than as an
/// opaque positional tool name. That is what makes the report able to separate "the
/// orchestrator never handed off" from "the orchestrator handed off and the
/// specialist mishandled the turn" — the two failures that look identical in the
/// report without it, since a handoff contributes no entry to
/// <see cref="AgentTurnContext.ToolCalls"/>.
/// </para>
/// </remarks>
internal sealed class HandoffRecordingChatClient : DelegatingChatClient
{
    private readonly string _fromSlug;
    private readonly IReadOnlyList<string> _participantSlugs;
    private readonly AgentTurnContext _turn;

    public HandoffRecordingChatClient(
        IChatClient inner,
        string fromSlug,
        IReadOnlyList<string> participantSlugs,
        AgentTurnContext turn)
        : base(inner)
    {
        _fromSlug = fromSlug;
        _participantSlugs = participantSlugs;
        _turn = turn;
    }

    public override async Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        Record(response.Messages);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken)
                           .ConfigureAwait(false))
        {
            Record(update.Contents);
            yield return update;
        }
    }

    private void Record(IList<ChatMessage>? messages)
    {
        if (messages is null)
        {
            return;
        }

        foreach (var message in messages)
        {
            Record(message.Contents);
        }
    }

    private void Record(IList<AIContent>? contents)
    {
        if (contents is null)
        {
            return;
        }

        foreach (var content in contents)
        {
            if (content is not FunctionCallContent call)
            {
                continue;
            }

            var to = HandoffToolName.ResolveTarget(call.Name, _participantSlugs);
            if (to is not null)
            {
                _turn.RecordHandoff(_fromSlug, to);
            }
        }
    }
}
