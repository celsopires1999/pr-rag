using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrRag.Application.Abstractions;
using PrRag.Application.Configuration;
using PrRag.Application.DTOs;
using ChatResponse = PrRag.Application.DTOs.ChatResponse;

namespace PrRag.Application.Services;

public sealed class ChatService : IChatService
{
    private readonly IChatClient _chatClient;
    private readonly IEmbeddingService _embeddingService;
    private readonly IPurchaseRequisitionRepository _repository;
    private readonly IRagReportWriter _reportWriter;
    private readonly ILogger<ChatService> _logger;
    private readonly RagSettings _ragSettings;

    private readonly Dictionary<string, AIFunction> _functions = new(StringComparer.Ordinal);
    private readonly IList<AITool> _tools = new List<AITool>();

    private int _activeTopK;
    private double _activeMinSimilarity;
    private string? _activeRewrittenQuery;

    public ChatService(
        IChatClient chatClient,
        IEmbeddingService embeddingService,
        IPurchaseRequisitionRepository repository,
        IRagReportWriter reportWriter,
        ILogger<ChatService> logger,
        IOptions<RagSettings> ragSettings)
    {
        _chatClient = chatClient;
        _embeddingService = embeddingService;
        _repository = repository;
        _reportWriter = reportWriter;
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

        var messages = BuildConversation(request.Question, null);
        var resolved = await ResolveContextAsync(messages, topK, minSimilarity, cancellationToken);
        report.RetrievedCount = resolved.RetrievedItems.Count;
        report.UsedNoContextFallback = resolved.RetrievedItems.Count == 0;
        report.RewrittenQuery = _activeRewrittenQuery;

        var answer = resolved.FinalMessage.Text;
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

        var messages = BuildConversation(request.Question, request.Messages);
        var resolved = await ResolveContextAsync(messages, topK, minSimilarity, cancellationToken);
        report.RetrievedCount = resolved.RetrievedItems.Count;
        report.UsedNoContextFallback = resolved.RetrievedItems.Count == 0;
        report.RewrittenQuery = _activeRewrittenQuery;

        var answer = resolved.FinalMessage.Text;
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
                            new FunctionResultContent(call.CallId, "Unknown tool. Use search_by_codes or search_semantic."),
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

    private List<ChatMessage> BuildConversation(string question, IReadOnlyList<ChatMessageDto>? history)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
        };

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

        When calling search_semantic, first rewrite the user question into a short, keyword-rich query optimized for cosine similarity search against the fields above. Use the full conversation history to disambiguate references such as "that one", "the other", "as we saw earlier", etc. Resolve those references against the earlier turns and incorporate the resolved entities into the query. IMPORTANT: The query must be in english.

        For each turn, follow this loop:
        1. Thought: reason about what the user is asking and what context you already have.
        2. Action: choose one action from the allowed vocabulary below.
        3. Observation: review the result returned by the action before deciding the next step.
        Repeat until you can answer. Once you have enough context, issue a Final Answer and stop calling tools.

        Allowed Actions:
        - Call a tool by name with its arguments (for example "search_by_codes", "search_semantic").
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

        IMPORTANT: Neve make suppositions or hallucinate information. If you don't know the answer, say "I don't have enough information to answer that."

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
