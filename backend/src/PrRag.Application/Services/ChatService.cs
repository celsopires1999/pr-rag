using System.Text.RegularExpressions;
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
    private readonly IChatClient _chatClient;
    private readonly IEmbeddingService _embeddingService;
    private readonly IPurchaseRequisitionRepository _repository;
    private readonly IRagReportWriter _reportWriter;
    private readonly ISkillService _skillService;
    private readonly IRequisitionWriter _requisitionWriter;
    private readonly ILogger<ChatService> _logger;
    private readonly RagSettings _ragSettings;

    private readonly Dictionary<string, AIFunction> _functions = new(StringComparer.Ordinal);
    private readonly IList<AITool> _tools = new List<AITool>();

    private int _activeTopK;
    private double _activeMinSimilarity;
    private string? _activeRewrittenQuery;
    private string? _activeSkillId;
    private string? _activeSkillName;
    private bool _skillActivated;
    private bool _requisitionCreated;

    private static readonly Regex SkillMarkerRegex = new(
        @"^\[Skill:\s*([\w-]+)\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public ChatService(
        IChatClient chatClient,
        IEmbeddingService embeddingService,
        IPurchaseRequisitionRepository repository,
        IRagReportWriter reportWriter,
        ISkillService skillService,
        IRequisitionWriter requisitionWriter,
        ILogger<ChatService> logger,
        IOptions<RagSettings> ragSettings)
    {
        _chatClient = chatClient;
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
            "Persists a new purchase requisition to disk as a JSON file. Call it ONLY after the user has explicitly confirmed the drafted requisition; never invent field values — use exactly the values the user provided and validated. Required parameters: supplierCode, item, description, quantity (a positive number), date (ISO format yyyy-MM-dd), requester.",
            (string supplierCode, string item, string description, decimal quantity, string date, string requester, CancellationToken ct) =>
                CreateRequisitionAsync(supplierCode, item, description, quantity, date, requester, ct));
    }

    public async Task<ChatResponse> AnswerAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        var topKFromRequest = request.TopK > 0;
        var minSimilarityFromRequest = request.MinSimilarity > 0;
        var topK = topKFromRequest ? request.TopK : _ragSettings.TopK;
        var minSimilarity = minSimilarityFromRequest ? request.MinSimilarity : _ragSettings.MinSimilarity;

        var report = new RagQueryReport
        {
            Question = request.Question,
            TopK = topK,
            MinSimilarity = minSimilarity,
            TopKFromRequest = topKFromRequest,
            MinSimilarityFromRequest = minSimilarityFromRequest,
        };

        ResetSkillState();
        var messages = await BuildConversationAsync(request.Question, null, cancellationToken);
        var resolved = await ResolveContextAsync(messages, topK, minSimilarity, cancellationToken);
        report.RetrievedCount = resolved.RetrievedItems.Count;
        report.UsedNoContextFallback = resolved.RetrievedItems.Count == 0;
        report.RewrittenQuery = _activeRewrittenQuery;
        report.SkillId = _activeSkillId;
        report.SkillName = _activeSkillName;
        report.SkillActivated = _skillActivated;

        var answer = ApplySkillMarker(resolved.FinalMessage.Text);
        report.Answer = answer;
        report.RetrievedItems = resolved.RetrievedItems;

        await WriteReportAsync(report, cancellationToken);

        return new ChatResponse
        {
            Answer = answer,
            RetrievedCount = resolved.RetrievedItems.Count,
        };
    }

    public async IAsyncEnumerable<string> StreamAsync(
        ChatStreamRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var topKFromRequest = request.TopK > 0;
        var minSimilarityFromRequest = request.MinSimilarity > 0;
        var topK = topKFromRequest ? request.TopK : _ragSettings.TopK;
        var minSimilarity = minSimilarityFromRequest ? request.MinSimilarity : _ragSettings.MinSimilarity;

        var report = new RagQueryReport
        {
            Question = request.Question,
            TopK = topK,
            MinSimilarity = minSimilarity,
            TopKFromRequest = topKFromRequest,
            MinSimilarityFromRequest = minSimilarityFromRequest,
        };

        ResetSkillState();
        var messages = await BuildConversationAsync(request.Question, request.Messages, cancellationToken);
        var resolved = await ResolveContextAsync(messages, topK, minSimilarity, cancellationToken);
        report.RetrievedCount = resolved.RetrievedItems.Count;
        report.UsedNoContextFallback = resolved.RetrievedItems.Count == 0;
        report.RewrittenQuery = _activeRewrittenQuery;
        report.SkillId = _activeSkillId;
        report.SkillName = _activeSkillName;
        report.SkillActivated = _skillActivated;

        var answer = ApplySkillMarker(resolved.FinalMessage.Text);
        report.Answer = answer;
        report.RetrievedItems = resolved.RetrievedItems;

        yield return answer;

        await WriteReportAsync(report, cancellationToken);
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
        _functions[name] = function;
        _tools.Add(function);
    }

    private async Task<ResolvedContext> ResolveContextAsync(
        List<ChatMessage> messages,
        int topK,
        double minSimilarity,
        CancellationToken cancellationToken)
    {
        _activeTopK = topK;
        _activeMinSimilarity = minSimilarity;
        _activeRewrittenQuery = null;

        var retrieved = new List<RagRetrievedItem>();
        var options = new ChatOptions
        {
            Tools = _tools,
            ToolMode = ChatToolMode.Auto,
        };

        while (true)
        {
            var response = await _chatClient.GetResponseAsync(messages, options, cancellationToken);
            var assistant = response.Messages.LastOrDefault(m => m.Role == ChatRole.Assistant);
            if (assistant is null)
            {
                break;
            }

            var calls = assistant.Contents
                .OfType<FunctionCallContent>()
                .Where(c => !c.InformationalOnly)
                .ToList();

            messages.Add(assistant);

            if (calls.Count == 0)
            {
                return new ResolvedContext(assistant, retrieved);
            }

            foreach (var call in calls)
            {
                if (_functions.TryGetValue(call.Name, out var function))
                {
                    var result = await InvokeFunctionAsync(function, call, cancellationToken);
                    if (result is IReadOnlyList<RagRetrievedItem> items)
                    {
                        retrieved.AddRange(items);
                    }

                    messages.Add(new ChatMessage(
                        ChatRole.Tool,
                        new List<AIContent> { new FunctionResultContent(call.CallId, result) }));
                }
                else
                {
                    messages.Add(new ChatMessage(
                        ChatRole.Tool,
                        new List<AIContent>
                        {
                            new FunctionResultContent(call.CallId, "Unknown tool. Use search_by_codes, search_semantic, activate_skill or create_requisition."),
                        }));
                }
            }
        }

        var fallback = new ChatMessage(
            ChatRole.Assistant,
            "I don't have enough information to answer that.");
        return new ResolvedContext(fallback, retrieved);
    }

    private async Task<object> InvokeFunctionAsync(
        AIFunction function,
        FunctionCallContent call,
        CancellationToken cancellationToken)
    {
        var arguments = new AIFunctionArguments();

        if (call.Arguments is { Count: > 0 } args)
        {
            foreach (var pair in args)
            {
                arguments[pair.Key] = pair.Value;
            }
        }

        return (await function.InvokeAsync(arguments, cancellationToken)) ?? string.Empty;
    }

    private async Task<IReadOnlyList<RagRetrievedItem>> SearchByCodesAsync(
        IReadOnlyList<string>? items,
        IReadOnlyList<string>? suppliers,
        CancellationToken cancellationToken)
    {
        var results = await _repository.SearchByCodesAsync(items, suppliers, _activeTopK, cancellationToken);
        return results.Select(r => RagRetrievedItem.From(r, null)).ToList();
    }

    private async Task<IReadOnlyList<RagRetrievedItem>> SearchSemanticAsync(
        string query,
        CancellationToken cancellationToken)
    {
        _activeRewrittenQuery = query;

        var embedding = await _embeddingService.GenerateAsync(query, cancellationToken);
        var results = await _repository.SearchAsync(embedding, _activeTopK, _activeMinSimilarity, cancellationToken);
        return results.Select(r => RagRetrievedItem.From(r.Requisition, r.Similarity)).ToList();
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

        _activeSkillId = skill.Name;
        _activeSkillName = skill.Name;
        _skillActivated = true;
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
        var result = await _requisitionWriter.WriteAsync(new NewPurchaseRequisition
        {
            SupplierCode = supplierCode,
            Item = item,
            Description = description,
            Quantity = quantity,
            Date = date,
            Requester = requester,
        }, cancellationToken);

        if (result.Success)
        {
            _requisitionCreated = true;
            return $"Requisition created: {result.FileName}.";
        }

        return result.Error!;
    }

    private async Task<List<ChatMessage>> BuildConversationAsync(
        string question,
        IReadOnlyList<ChatMessageDto>? history,
        CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, BuildSystemPrompt()),
        };

        var restoredSkill = await FindActiveSkillFromHistoryAsync(history, cancellationToken);
        if (restoredSkill is not null)
        {
            _activeSkillId = restoredSkill.Name;
            _activeSkillName = restoredSkill.Name;
            _skillActivated = true;
            messages.Add(new ChatMessage(ChatRole.System, restoredSkill.Body));
        }

        if (history is not null)
        {
            foreach (var msg in history)
            {
                var role = string.Equals(msg.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                    ? ChatRole.Assistant
                    : ChatRole.User;
                if (role == ChatRole.Assistant || role == ChatRole.User)
                {
                    messages.Add(new ChatMessage(role, msg.Content));
                }
            }
        }

        messages.Add(new ChatMessage(ChatRole.User, question));
        return messages;
    }

    /// <summary>
    /// Restores an activated skill on a subsequent turn. The chat history the
    /// client sends is plain text, so the assistant marks an active skill with a
    /// leading "[Skill: &lt;name&gt;]" line, which is parsed back here and the
    /// skill's guidance is re-injected as a system message.
    /// </summary>
    private async Task<Skill?> FindActiveSkillFromHistoryAsync(
        IReadOnlyList<ChatMessageDto>? history,
        CancellationToken cancellationToken)
    {
        if (history is null)
        {
            return null;
        }

        for (var i = history.Count - 1; i >= 0; i--)
        {
            var message = history[i];
            if (!string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var match = SkillMarkerRegex.Match(message.Content ?? string.Empty);
            if (match.Success)
            {
                return await _skillService.GetSkillAsync(match.Groups[1].Value, cancellationToken);
            }
        }

        return null;
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

            Marker rule (required): while a skill is active, your reply MUST start with the exact line
            "[Skill: <skill-name>]" (skill name in place of the placeholder) followed by your actual
            answer on the next line. The marker is how the guided workflow persists across turns. Stop
            using the marker once the skill's workflow is complete — for example, right after successfully
            calling create_requisition.
            """;
    }

    /// <summary>
    /// Guarantees the leading "[Skill: &lt;name&gt;]" marker on the returned answer
    /// while a skill is active, so the plain-text history can restore the
    /// guidance on the following turn. The marker is dropped once the skill's
    /// deliverable (the requisition) has been persisted.
    /// </summary>
    private string ApplySkillMarker(string answer)
    {
        if (!_skillActivated || _activeSkillName is null || _requisitionCreated)
        {
            return answer;
        }

        if (SkillMarkerRegex.IsMatch(answer))
        {
            return answer;
        }

        return $"[Skill: {_activeSkillName}]\n{answer}";
    }

    private void ResetSkillState()
    {
        _activeSkillId = null;
        _activeSkillName = null;
        _skillActivated = false;
        _requisitionCreated = false;
    }

    private async Task WriteReportAsync(RagQueryReport report, CancellationToken cancellationToken)
    {
        try
        {
            await _reportWriter.WriteAsync(report, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write RAG observability report");
        }
    }

    private const string SystemPrompt =
        """
        You are a helpful assistant answering questions about purchase requisitions.
        Think step by step using a reasoning loop: alternate between a Thought, an Action, and an Observation until you can produce a final answer.

        Tools:
        - search_by_codes: use it when the user references exact ITM-* item codes or SUP* supplier codes.
        - search_semantic: use it when the user asks about requisitions by meaning or description.
        - activate_skill: use it when the user's request matches the intent of one of the available skills listed in the Skills section of this prompt. It loads that skill's instructions into the conversation to guide the workflow.
        - create_requisition: use it ONLY after the user has explicitly confirmed a drafted purchase requisition, to persist the requisition to disk as a JSON file. Never invent field values; use exactly the values the user provided and that you validated. Required parameters: supplierCode, item, description, quantity (a positive number), date (ISO format yyyy-MM-dd), requester.

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

    private sealed record ResolvedContext(
        ChatMessage FinalMessage,
        List<RagRetrievedItem> RetrievedItems);
}