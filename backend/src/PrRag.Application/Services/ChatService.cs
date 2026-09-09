using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrRag.Application.Abstractions;
using PrRag.Application.Configuration;
using PrRag.Application.DTOs;
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
    private readonly ISkillService _skillService;
    private readonly AgentTurnContext _turnContext;
    private readonly IRagReportWriter _reportWriter;
    private readonly ILogger<ChatService> _logger;
    private readonly RagSettings _ragSettings;

    public ChatService(
        IAgentRunService runService,
        IAgentSessionStore sessionStore,
        ISkillService skillService,
        AgentTurnContext turnContext,
        IRagReportWriter reportWriter,
        ILogger<ChatService> logger,
        IOptions<RagSettings> ragSettings)
    {
        _runService = runService;
        _sessionStore = sessionStore;
        _skillService = skillService;
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

        var (session, created) = await _sessionStore.GetOrCreateAsync(
            sessionId,
            () => _runService.CreateSessionAsync(cancellationToken),
            cancellationToken);

        _turnContext.Begin(session, sessionId, topK, minSimilarity);

        var messages = BuildTurnMessages(request.Question, created);
        var response = await _runService.RunAsync(messages, session, cancellationToken);

        var answer = response.Text;
        await WriteReportAsync(answer, request.Question, topK, minSimilarity, topKFromRequest, minSimilarityFromRequest, cancellationToken);

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

        var (session, created) = await _sessionStore.GetOrCreateAsync(
            sessionId,
            () => _runService.CreateSessionAsync(cancellationToken),
            cancellationToken);

        _turnContext.Begin(session, sessionId, topK, minSimilarity);

        var messages = BuildTurnMessages(request.Question, created);
        var latestText = new System.Text.StringBuilder();
        await foreach (var update in _runService.RunStreamingAsync(messages, session, cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                latestText.Append(update.Text);
                yield return update.Text;
            }
        }

        await WriteReportAsync(latestText.ToString(), request.Question, topK, minSimilarity, topKFromRequest, minSimilarityFromRequest, cancellationToken);
    }

    private static string ResolveSessionId(string? requested)
    {
        return string.IsNullOrWhiteSpace(requested) ? Guid.NewGuid().ToString("N") : requested.Trim();
    }

    /// <summary>
    /// Builds the messages for a single turn. Only a brand-new session receives
    /// the system prompt; the <see cref="Microsoft.Agents.AI.AgentSession"/>
    /// accumulates history across runs, so later turns only need the current
    /// user message. An active skill whose guidance has not been injected yet is
    /// re-injected as an extra system message.
    /// </summary>
    private List<ChatMessage> BuildTurnMessages(string question, bool created)
    {
        var messages = new List<ChatMessage>();
        if (created)
        {
            messages.Add(new ChatMessage(ChatRole.System, AgentInstructions.ComposeSystemPrompt(_skillService.GetManifest())));
        }

        var session = _turnContext.Session!;
        var skillBody = SkillSessionState.ReadActiveSkillBody(session);
        if (skillBody is not null)
        {
            messages.Add(new ChatMessage(ChatRole.System, skillBody));
            SkillSessionState.MarkInjected(session);
        }

        messages.Add(new ChatMessage(ChatRole.User, question));
        return messages;
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
        var (skillId, skillActivated) = SkillSessionState.DescribeForReport(_turnContext.Session);
        var report = new RagQueryReport
        {
            Question = question,
            TopK = topK,
            MinSimilarity = minSimilarity,
            TopKFromRequest = topKFromRequest,
            MinSimilarityFromRequest = minSimilarityFromRequest,
            RetrievedCount = _turnContext.RetrievedItems.Count,
            UsedNoContextFallback = _turnContext.RetrievedItems.Count == 0,
            RewrittenQuery = _turnContext.RewrittenQuery,
            SkillId = skillId,
            SkillName = skillId,
            SkillActivated = skillActivated,
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