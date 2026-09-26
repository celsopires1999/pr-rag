using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrRag.Application.Abstractions;
using PrRag.Application.Configuration;
using PrRag.Application.DTOs;
using PrRag.Application.Domain;
using PrRag.Application.Services.Agents;
using ChatResponse = PrRag.Application.DTOs.ChatResponse;

namespace PrRag.Application.Services;

/// <summary>
/// Thin orchestration adapter: resolves the session, assembles each turn's
/// messages, delegates the agent run to <see cref="IAgentRunService"/>, and
/// writes the RAG observability report. Agent construction, tool registration,
/// prompt compilation, and skill-state keys live elsewhere.
/// </summary>
public sealed class ChatService : IChatService
{
    private readonly IAgentRunService _runService;
    private readonly IAgentSessionStore _sessionStore;
    private readonly AgentTurnContext _turnContext;
    private readonly IRagReportWriter _reportWriter;
    private readonly ILogger<ChatService> _logger;
    private readonly RagSettings _ragSettings;

    public ChatService(
        IAgentRunService runService,
        IAgentSessionStore sessionStore,
        AgentTurnContext turnContext,
        IRagReportWriter reportWriter,
        ILogger<ChatService> logger,
        IOptions<RagSettings> ragSettings)
    {
        _runService = runService;
        _sessionStore = sessionStore;
        _turnContext = turnContext;
        _reportWriter = reportWriter;
        _logger = logger;
        _ragSettings = ragSettings.Value;
    }

    public async Task<ChatResponse> AnswerAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        var sessionId = ResolveSessionId(request.SessionId);
        var topKFromRequest = request.TopK > 0;
        var minSimilarityFromRequest = request.MinSimilarity > 0;
        var topK = topKFromRequest ? request.TopK : _ragSettings.TopK;
        var minSimilarity = minSimilarityFromRequest ? request.MinSimilarity : _ragSettings.MinSimilarity;

        var state = _sessionStore.GetOrCreate(sessionId);
        var session = await _sessionStore.BeginTurnAsync(
            sessionId,
            () => _runService.CreateSessionAsync(cancellationToken),
            cancellationToken);

        _turnContext.Begin(session, state, sessionId, topK, minSimilarity);

        var messages = BuildTurnMessages(state, request.Question);
        var response = await _runService.RunAsync(messages, session, cancellationToken);

        var answer = response.Text;
        await WriteReportAsync(answer, request.Question, topK, minSimilarity, topKFromRequest, minSimilarityFromRequest, cancellationToken);

        // An empty run is a failure, not a quiet turn. The framework completes a
        // run whose chat call threw — a rejected key, a provider outage — and
        // surfaces nothing, so without this the caller gets 200 with a blank
        // answer and a report claiming a fallback that was never written. The
        // report is written first so the turn is still diagnosable, and the
        // history is left alone: recording a question with an empty answer would
        // replay a broken turn into every later turn of the session.
        RequireAnswer(answer);

        RecordTurn(state, request.Question, answer);

        return new ChatResponse
        {
            Answer = answer,
            RetrievedCount = _turnContext.RetrievedItems.Count,
            SessionId = sessionId,
        };
    }

    public async IAsyncEnumerable<string> StreamAsync(
        ChatStreamRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sessionId = ResolveSessionId(request.SessionId);
        var topKFromRequest = request.TopK > 0;
        var minSimilarityFromRequest = request.MinSimilarity > 0;
        var topK = topKFromRequest ? request.TopK : _ragSettings.TopK;
        var minSimilarity = minSimilarityFromRequest ? request.MinSimilarity : _ragSettings.MinSimilarity;

        var state = _sessionStore.GetOrCreate(sessionId);
        var session = await _sessionStore.BeginTurnAsync(
            sessionId,
            () => _runService.CreateSessionAsync(cancellationToken),
            cancellationToken);

        _turnContext.Begin(session, state, sessionId, topK, minSimilarity);

        var messages = BuildTurnMessages(state, request.Question);
        var latestText = new System.Text.StringBuilder();
        await foreach (var update in _runService.RunStreamingAsync(messages, session, cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                latestText.Append(update.Text);
                yield return update.Text;
            }
        }

        var streamed = latestText.ToString();
        await WriteReportAsync(streamed, request.Question, topK, minSimilarity, topKFromRequest, minSimilarityFromRequest, cancellationToken);

        // Checked after the fact because a stream cannot change its status once
        // the headers are out: throwing here aborts the response, where returning
        // cleanly would send [DONE] after no content and the client would render a
        // completed, empty answer.
        RequireAnswer(streamed);

        RecordTurn(state, request.Question, streamed);
    }

    /// <summary>
    /// Fails the turn when the run produced no text.
    /// </summary>
    ///
    /// <para>
    /// This is a domain invariant rather than a defensive check: there is no
    /// successful turn whose answer is blank, so a blank run means the chat client
    /// threw and the framework absorbed the error. The original exception is not
    /// available here, so the message points at the framework's log rather than
    /// inventing a cause.
    /// </para>
    /// </summary>
    private static void RequireAnswer(string? answer)
    {
        if (!string.IsNullOrWhiteSpace(answer))
        {
            return;
        }

        throw new ChatTurnFailedException(
            "The agent run completed without producing any answer text. The chat client most " +
            "likely failed and the framework absorbed the error; the cause is in the log entry " +
            "for Microsoft.Extensions.AI.LoggingChatClient on this request.");
    }

    private static string ResolveSessionId(string? requested)
    {
        return string.IsNullOrWhiteSpace(requested) ? Guid.NewGuid().ToString("N") : requested.Trim();
    }

    /// <summary>
    /// Builds the messages for a single turn: the conversation so far, then the
    /// current question. The system prompt is not built here — it is supplied by
    /// the agent's own instructions, which are recomposed per request and so
    /// cannot go stale when the skill manifest changes under a live session.
    /// </summary>
    ///
    /// <para>
    /// The agents' own history providers are inert, so this list is the only
    /// history they see. The active skill's guidance is prepended as a system
    /// message on the turn after activation, and only that turn: the model has to
    /// call <c>activate_skill</c> first and see what it returned, but restating
    /// the guidance on every later turn would never let the workflow end.
    /// </para>
    private List<ChatMessage> BuildTurnMessages(AgentSessionState state, string question)
    {
        var messages = new List<ChatMessage>(state.History);

        var skillBody = SkillSessionState.ReadActiveSkillBody(state);
        if (skillBody is not null)
        {
            messages.Add(new ChatMessage(ChatRole.System, skillBody));
            SkillSessionState.MarkInjected(state);
        }

        messages.Add(new ChatMessage(ChatRole.User, question));
        return messages;
    }

    /// <summary>
    /// Appends the finished turn to the conversation.
    /// </summary>
    ///
    /// <para>
    /// Only the answer text is recorded, never the reply messages. The rows a
    /// search returned reach the next turn because the answer carries them, not
    /// because the tool traffic is replayed: half a tool transcript is not
    /// well-formed on its own (across a handoff the specialist makes the call, and
    /// the framework batches the results into a later message), and replaying
    /// either half is rejected by the chat API outright. See D12.
    /// </para>
    ///
    /// <para>
    /// Only the delta is appended, and only what the agent produced. The reply
    /// echoes the request — this question and every earlier turn — so recording it
    /// unfiltered grows the history geometrically: a third turn would open with
    /// the same question three times, which the model then answers by repeating
    /// itself. Anything already sent is dropped, matched on content rather than
    /// identity because the framework hands the reply back as copies.
    /// </para>
    /// <summary>
    /// Appends one turn to the conversation as text.
    ///
    /// <para>
    /// The run's own messages are deliberately not consulted. They arrive as
    /// the request replayed back followed by this turn's output, and no
    /// reassembly of them is replayable: a tool result is only valid if the
    /// call that produced it is replayed first, and across a handoff the
    /// specialist made the call, so the workflow surfaces results alone. The
    /// framework also batches results into a single message whose last call is
    /// emitted as a <em>later</em> assistant message, so no prefix of a tool
    /// transcript is well-formed. Replaying either half is rejected by the chat
    /// API outright and fails the whole turn.
    /// </para>
    ///
    /// <para>
    /// The answer text is the run's, so the recorded assistant turn is exactly
    /// what the caller was shown. The answer already carries the grounded rows,
    /// so the conversation stays answerable from text alone.
    /// </para>
    /// </summary>
    private static void RecordTurn(AgentSessionState state, string question, string answer)
    {
        state.History.Add(new ChatMessage(ChatRole.User, question));
        state.History.Add(new ChatMessage(ChatRole.Assistant, answer));
    }


    private async Task WriteReportAsync(
        string? answer,
        string question,
        int topK,
        double minSimilarity,
        bool topKFromRequest,
        bool minSimilarityFromRequest,
        CancellationToken cancellationToken)
    {
        var (skillId, skillActivated) = SkillSessionState.DescribeForReport(_turnContext.State);
        var report = new RagQueryReport
        {
            Question = question,
            TopK = topK,
            MinSimilarity = minSimilarity,
            TopKFromRequest = topKFromRequest,
            MinSimilarityFromRequest = minSimilarityFromRequest,
            RetrievedCount = _turnContext.RetrievedItems.Count,

            // This flag says the caller was given the no-context answer, so it
            // cannot be true when no answer was produced at all. RetrievedCount ==
            // 0 is the retrieval condition, not proof the fallback was sent: a
            // failed run also retrieves nothing, and reporting a fallback there
            // made a dead provider look like an ordinary empty result.
            UsedNoContextFallback = _turnContext.RetrievedItems.Count == 0
                && !string.IsNullOrWhiteSpace(answer),

            RetrievalAttempted = _turnContext.ToolCalls.Any(t => ToolNames.ReadOnly.Contains(t.Name)),
            RewrittenQuery = _turnContext.RewrittenQuery,
            SkillId = skillId,
            SkillName = skillId,
            SkillActivated = skillActivated,
            RequisitionDraftStaged = _turnContext.DraftStaged,
            RequisitionDraftPresented = _turnContext.DraftPresented,
            RequisitionDraftConfirmed = _turnContext.DraftConfirmed,
            RequisitionPersisted = _turnContext.RequisitionPersisted,
            Answer = answer ?? string.Empty,
            RetrievedItems = _turnContext.RetrievedItems,
            ToolCalls = _turnContext.ToolCalls,
        };

        try
        {
            await _reportWriter.WriteAsync(report, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write RAG observability report");
        }
    }
}