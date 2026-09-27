using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using PrRag.Application.DTOs;

namespace PrRag.Application.Services.Agents;

/// <summary>
/// Attributes a turn's model traffic to the agent that produced it: the handoffs
/// that agent made, and — on the response that carried the answer — the authorship
/// of that answer.
/// </summary>
/// <remarks>
/// <para>
/// Both facts are observed in the same place and for the same reason, which is
/// that this is the only place either is observable. A handoff is not a tool the
/// application registers, so nothing else can see one: it arrives as a
/// <see cref="FunctionCallContent"/> in the model's own response — the framework
/// turns it into a graph edge afterwards — which means the only place it is visible
/// is on the way out of the chat client, before the function-invocation layer
/// dispatches it. The answer's author is the same shape of problem one layer up:
/// the run's text is the concatenation of whatever the agents said, so which agent
/// said the last of it is only knowable where an agent's own traffic is still
/// identifiable. A tool handler would run after both decisions, and a
/// session-level observer would run after the answer.
/// </para>
/// <para>
/// The agent is known because the composer binds this decorator per agent, so a
/// handoff is recorded as a from/to pair of capability slugs rather than as an
/// opaque positional tool name, and an answer is attributed to a capability rather
/// than to "the workflow". That is what makes the report able to separate "the
/// orchestrator never handed off" from "the orchestrator handed off and the
/// specialist mishandled the turn" — the two failures that look identical in the
/// report without it, since a handoff contributes no entry to
/// <see cref="AgentTurnContext.ToolCalls"/> and neither does an answer.
/// </para>
/// <para>
/// Ordering is the run's, not this class's. Every agent's instance writes the one
/// <see cref="AgentTurnContext.AnswerAgent"/> field on the shared turn context and
/// the last write stands, so no coordination between instances is needed and the
/// surviving value is the last agent that had text to give — see
/// <see cref="RagQueryReport.AnswerAgent"/> for why that is the answer's author
/// rather than a proxy for it.
/// </para>
/// </remarks>
internal sealed class AgentAttributionChatClient : DelegatingChatClient
{
    private readonly string _fromSlug;
    private readonly IReadOnlyList<string> _participantSlugs;
    private readonly AgentTurnContext _turn;

    public AgentAttributionChatClient(
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
        RecordHandoffCalls(response.Messages);

        // Text, not a message count or a call: a response that carries only a
        // function call says nothing, and attributing the turn to whichever agent
        // happened to call a tool last would report a mid-loop step as the author
        // of the answer the caller receives.
        if (!string.IsNullOrWhiteSpace(response.Text))
        {
            _turn.RecordAnswerAuthor(_fromSlug);
        }

        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Streamed text arrives in pieces, and a single piece is not an answer, so
        // attribution is decided per response rather than per update: the first
        // non-empty piece records this agent, and the field's last-write-wins
        // semantics leave whichever agent spoke last holding it.
        var saidAnything = false;

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken)
                           .ConfigureAwait(false))
        {
            RecordHandoffCalls(update.Contents);

            if (!string.IsNullOrEmpty(update.Text))
            {
                saidAnything = true;
            }

            yield return update;
        }

        if (saidAnything)
        {
            _turn.RecordAnswerAuthor(_fromSlug);
        }
    }

    private void RecordHandoffCalls(IList<ChatMessage>? messages)
    {
        if (messages is null)
        {
            return;
        }

        foreach (var message in messages)
        {
            RecordHandoffCalls(message.Contents);
        }
    }

    private void RecordHandoffCalls(IList<AIContent>? contents)
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
