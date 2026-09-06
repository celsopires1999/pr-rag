using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrRag.Application.Abstractions;
using PrRag.Application.Configuration;
using PrRag.Application.Domain;
using PrRag.Application.DTOs;
using ChatResponse = PrRag.Application.DTOs.ChatResponse;

namespace PrRag.Application.Services;

public sealed class ChatService : IChatService
{
    private const string SkillIdKey = "SkillId";
    private const string SkillBodyKey = "SkillBody";
    private const string SkillBodyInjectedKey = "SkillBodyInjected";

    private readonly ChatClientAgent _agent;
    private readonly IAgentSessionStore _sessionStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly IPurchaseRequisitionRepository _repository;
    private readonly IRagReportWriter _reportWriter;
    private readonly ISkillService _skillService;
    private readonly IRequisitionWriter _requisitionWriter;
    private readonly ILogger<ChatService> _logger;
    private readonly RagSettings _ragSettings;

    private readonly IList<AITool> _tools = new List<AITool>();

    private AgentSession? _activeSession;
    private int _activeTopK;
    private double _activeMinSimilarity;
    private string? _activeRewrittenQuery;
    private readonly List<RagRetrievedItem> _activeRetrievedItems = new();

    public ChatService(
        IChatClient chatClient,
        IAgentSessionStore sessionStore,
        IEmbeddingService embeddingService,
        IPurchaseRequisitionRepository repository,
        IRagReportWriter reportWriter,
        ISkillService skillService,
        IRequisitionWriter requisitionWriter,
        ILogger<ChatService> logger,
        IOptions<RagSettings> ragSettings)
    {
        _sessionStore = sessionStore;
        _embeddingService = embeddingService;
        _repository = repository;
        _reportWriter = reportWriter;
        _skillService = skillService;
        _requisitionWriter = requisitionWriter;
        _logger = logger;
        _ragSettings = ragSettings.Value;

        RegisterFunction(
            "search_by_codes",
            "Search purchase requisitions by exact item codes (ITM-*) and/or supplier codes (SUP*). Returns matching requisitions with their supplier and item details.",
            (IReadOnlyList<string>? items = null, IReadOnlyList<string>? suppliers = null, CancellationToken ct = default) =>
                SearchByCodesAsync(items, suppliers, ct));

        RegisterFunction(
            "search_semantic",
            "Search purchase requisitions by semantic similarity to the given query text, in any language. Returns the most relevant requisitions.",
            (string query, CancellationToken ct) => SearchSemanticAsync(query, ct));

        RegisterFunction(
            "activate_skill",
            "Activates a skill by name to guide the conversation. Use it when the user's request matches the intent of one of the available skills listed in the Skills section. Returns the skill's instructions to follow. Unknown skills return an error listing the available skills.",
            (string name, CancellationToken ct) => ActivateSkillAsync(name, ct));

        RegisterFunction(
            "create_requisition",
            "Persists a new purchase requisition to disk as a JSON file. Call it ONLY after the user has explicitly confirmed the drafted requisition; never invent field values — use exactly the values the user provided and validated. Required parameters: supplierCode, item, description, quantity (a positive number), date (ISO format yyyy-MM-dd), requester. Refuses to create a requisition when no existing requisition has the same item + supplier combination.",
            (string supplierCode, string item, string description, decimal quantity, string date, string requester, CancellationToken ct) =>
                CreateRequisitionAsync(supplierCode, item, description, quantity, date, requester, ct));

        _agent = chatClient.AsAIAgent(
            name: "purchase-requisition-agent",
            description: "Answers questions about purchase requisitions and guides purchase-requisition workflows.");
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
            () => _agent.CreateSessionAsync(cancellationToken),
            cancellationToken);

        _activeSession = session;
        _activeTopK = topK;
        _activeMinSimilarity = minSimilarity;
        _activeRewrittenQuery = null;
        _activeRetrievedItems.Clear();

        var messages = BuildTurnMessages(request.Question, created);
        var response = await _agent.RunAsync(messages, session, BuildRunOptions(), cancellationToken);

        var answer = response.Text;
        await WriteReportAsync(answer, request.Question, topK, minSimilarity, topKFromRequest, minSimilarityFromRequest, cancellationToken);

        return new ChatResponse
        {
            Answer = answer,
            RetrievedCount = _activeRetrievedItems.Count,
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
            () => _agent.CreateSessionAsync(cancellationToken),
            cancellationToken);

        _activeSession = session;
        _activeTopK = topK;
        _activeMinSimilarity = minSimilarity;
        _activeRewrittenQuery = null;
        _activeRetrievedItems.Clear();

        var messages = BuildTurnMessages(request.Question, created);
        var latestText = new System.Text.StringBuilder();
        await foreach (var update in _agent.RunStreamingAsync(messages, session, BuildRunOptions(), cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                latestText.Append(update.Text);
                yield return update.Text;
            }
        }

        await WriteReportAsync(latestText.ToString(), request.Question, topK, minSimilarity, topKFromRequest, minSimilarityFromRequest, cancellationToken);
    }

    private ChatClientAgentRunOptions BuildRunOptions()
    {
        return new ChatClientAgentRunOptions(new ChatOptions
        {
            Tools = _tools,
            ToolMode = ChatToolMode.Auto,
        });
    }

    private static string ResolveSessionId(string? requested)
    {
        return string.IsNullOrWhiteSpace(requested) ? Guid.NewGuid().ToString("N") : requested.Trim();
    }

    /// <summary>
    /// Builds the messages for a single turn. Only a brand-new session receives
    /// the system prompt; the <see cref="AgentSession"/> accumulates history
    /// across runs, so later turns only need the current user message. An active
    /// skill whose guidance has not been injected yet is re-injected as an extra
    /// system message.
    /// </summary>
    private List<ChatMessage> BuildTurnMessages(string question, bool created)
    {
        var messages = new List<ChatMessage>();
        if (created)
        {
            messages.Add(new ChatMessage(ChatRole.System, BuildSystemPrompt()));
        }

        var skillBody = ReadActiveSkillBody(_activeSession!);
        if (skillBody is not null)
        {
            messages.Add(new ChatMessage(ChatRole.System, skillBody));
            _activeSession!.StateBag.SetValue(SkillBodyInjectedKey, "true");
        }

        messages.Add(new ChatMessage(ChatRole.User, question));
        return messages;
    }

    private void RegisterFunction(
        string name,
        string description,
        Delegate handler)
    {
        var function = AIFunctionFactory.Create(handler, new AIFunctionFactoryOptions
        {
            Name = name,
            Description = description,
            MarshalResult = (result, _, _) => new ValueTask<object?>(result),
        });
        _tools.Add(function);
    }

    private async Task<IReadOnlyList<RagRetrievedItem>> SearchByCodesAsync(
        IReadOnlyList<string>? items,
        IReadOnlyList<string>? suppliers,
        CancellationToken cancellationToken)
    {
        var results = await _repository.SearchByCodesAsync(items, suppliers, _activeTopK, cancellationToken);
        var mapped = results.Select(r => RagRetrievedItem.From(r, null)).ToList();
        _activeRetrievedItems.AddRange(mapped);
        return mapped;
    }

    private async Task<IReadOnlyList<RagRetrievedItem>> SearchSemanticAsync(
        string query,
        CancellationToken cancellationToken)
    {
        _activeRewrittenQuery = query;

        var embedding = await _embeddingService.GenerateAsync(query, cancellationToken);
        var results = await _repository.SearchAsync(embedding, _activeTopK, _activeMinSimilarity, cancellationToken);
        var mapped = results.Select(r => RagRetrievedItem.From(r.Requisition, r.Similarity)).ToList();
        _activeRetrievedItems.AddRange(mapped);
        return mapped;
    }

    private async Task<string> ActivateSkillAsync(string name, CancellationToken cancellationToken)
    {
        var skill = await _skillService.GetSkillAsync(name, cancellationToken);
        if (skill is null)
        {
            var available = _skillService.GetManifest();
            var names = available.Count == 0 ? "none" : string.Join(", ", available.Select(s => s.Name));
            return $"Unknown skill '{name}'. Available skills: {names}.";
        }

        _activeSession!.StateBag.SetValue(SkillIdKey, skill.Name);
        _activeSession.StateBag.SetValue(SkillBodyKey, skill.Body);
        _activeSession.StateBag.TryRemoveValue(SkillBodyInjectedKey);
        return skill.Body;
    }

    private async Task<string> CreateRequisitionAsync(
        string supplierCode,
        string item,
        string description,
        decimal quantity,
        string date,
        string requester,
        CancellationToken cancellationToken)
    {
        var requisition = new NewPurchaseRequisition
        {
            SupplierCode = supplierCode,
            Item = item,
            Description = description,
            Quantity = quantity,
            Date = date,
            Requester = requester,
        };

        var validationError = requisition.Validate();
        if (validationError is not null)
        {
            return validationError;
        }

        var combinationExists = await _repository.ExistsItemSupplierCombinationAsync(
            item,
            supplierCode,
            cancellationToken);

        if (!combinationExists)
        {
            return $"Cannot create requisition: supplier {supplierCode} has no recorded requisition for item {item}. The supplier is not registered for that item, so no requisition was created. Ask the user to confirm the item and supplier.";
        }

        var result = await _requisitionWriter.WriteAsync(requisition, cancellationToken);

        if (result.Success)
        {
            ClearSkillState(_activeSession!);
            return $"Requisition created: {result.FileName}.";
        }

        return result.Error!;
    }

    private static string? ReadActiveSkillBody(AgentSession session)
    {
        if (!session.StateBag.TryGetValue<string>(SkillIdKey, out var skillId)
            || string.IsNullOrWhiteSpace(skillId))
        {
            return null;
        }

        // Guidance already injected in an earlier turn of this session.
        if (session.StateBag.TryGetValue<string>(SkillBodyInjectedKey, out var injected) && injected == "true")
        {
            return null;
        }

        return session.StateBag.TryGetValue<string>(SkillBodyKey, out var body) ? body : null;
    }

    private static void ClearSkillState(AgentSession session)
    {
        session.StateBag.TryRemoveValue(SkillIdKey);
        session.StateBag.TryRemoveValue(SkillBodyKey);
        session.StateBag.TryRemoveValue(SkillBodyInjectedKey);
    }

    private string BuildSystemPrompt()
    {
        var manifest = _skillService.GetManifest();
        var skillsSection = manifest.Count == 0
            ? "No skills are available."
            : string.Join("\n", manifest.Select(s => $"- {s.Name}: {s.Description}"));

        return $"""
            {SystemPrompt}

            Skills:
            {skillsSection}

            Available skills guide recurring workflows. If the user's request matches a skill's intent, call
            activate_skill to load it and then follow its instructions step by step. A skill only adds
            conversational guidance; it NEVER adds, removes, or changes the tools available to you. If no
            skill matches, answer normally without activating one.
            """;
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
        var (skillId, skillActivated) = GetActiveSkillForReport(_activeSession);
        var report = new RagQueryReport
        {
            Question = question,
            TopK = topK,
            MinSimilarity = minSimilarity,
            TopKFromRequest = topKFromRequest,
            MinSimilarityFromRequest = minSimilarityFromRequest,
            RetrievedCount = _activeRetrievedItems.Count,
            UsedNoContextFallback = _activeRetrievedItems.Count == 0,
            RewrittenQuery = _activeRewrittenQuery,
            SkillId = skillId,
            SkillName = skillId,
            SkillActivated = skillActivated,
            Answer = answer ?? string.Empty,
            RetrievedItems = _activeRetrievedItems,
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

    private static (string? SkillName, bool Active) GetActiveSkillForReport(AgentSession? session)
    {
        if (session is null)
        {
            return (null, false);
        }

        if (!session.StateBag.TryGetValue<string>(SkillIdKey, out var skillId) || string.IsNullOrWhiteSpace(skillId))
        {
            return (null, false);
        }

        return (skillId, true);
    }

    private const string SystemPrompt =
        """
        You are a helpful assistant answering questions about purchase requisitions.
        Think step by step using a reasoning loop: alternate between a Thought, an Action, and an Observation until you can produce a final answer.

        Tools:
        - search_by_codes: use it when the user references exact ITM-* item codes or SUP* supplier codes.
        - search_semantic: use it when the user asks about requisitions by meaning or description.
        - activate_skill: use it when the user's request matches the intent of one of the available skills listed in the Skills section of this prompt. It loads that skill's instructions into the conversation to guide the workflow.
        - create_requisition: use it ONLY after the user has explicitly confirmed a drafted purchase requisition, to persist the requisition to disk as a JSON file. Never invent field values; use exactly the values the user provided and that you validated. Required parameters: supplierCode, item, description, quantity (a positive number), date (ISO format yyyy-MM-dd), requester. It refuses to create a requisition when no existing requisition has the same item + supplier combination.

        When calling search_semantic, first rewrite the user question into a short, keyword-rich query optimized for cosine similarity search against the fields above. Use the full conversation history to disambiguate references such as "that one", "the other", "as we saw earlier", etc. Resolve those references against the earlier turns and incorporate the resolved entities into the query. IMPORTANT: The query must be in english.

        For each turn, follow this loop:
        1. Thought: reason about what the user is asking and what context you already have.
        2. Action: choose one action from the allowed vocabulary below.
        3. Observation: review the result returned by the action before deciding the next step.
        Repeat until you can answer. Once you have enough context, issue a Final Answer and stop calling tools.

        Allowed Actions:
        - Call a tool by name with its arguments (for example "search_by_codes", "search_semantic", "activate_skill", "create_requisition").
        - "Final Answer" when the current context is sufficient to answer.

        Consider the following when reasoning about the user's question:
        - "item name" is the official name of the item in the requisition,
        - "item code" is the official code of the item in the requisition,
        - "description" is the free-text description the requisition which may include additional details about the item including its intended use, specifications, or other relevant information,
        - "supplier name" is the official name of the supplier in the requisition,
        - "supplier code" is the official code of the supplier in the requisition,

        Ground your answer on the requisitions returned by the tools you called. If prior conversation
        turns already contain the needed context, you may rely on that instead of calling a tool again.
        If no retrieval produces usable context, answer gracefully using what you know.
        Answer in the same language as the user.

        IMPORTANT: Never make suppositions or hallucinate information. If you don't know the answer, say "I don't have enough information to answer that."

        *** Guardrails ***
        - You are not able to determine which requisition is the newest because you do not have the date information;
        - You are not able to determine which requisition is the oldest because you do not have the date information;
        - You are not able to determine which requisition is the largest because you do not have the quantity information;
        - You are not able to determine which requisition is the smallest because you do not have the quantity information;
        - You are not able to determine which requisition is the most expensive because you do not have the price information;
        - You are not able to determine which requisition is the least expensive because you do not have the price information

        """;
}