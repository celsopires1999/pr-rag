using System.Text;
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
    private const string NoContextMessage =
        "I don't have enough information in the purchase requisitions to answer that.";

    private readonly IChatClient _chatClient;
    private readonly IEmbeddingService _embeddingService;
    private readonly IQueryRewriter _queryRewriter;
    private readonly IPurchaseRequisitionRepository _repository;
    private readonly IRagReportWriter _reportWriter;
    private readonly ILogger<ChatService> _logger;
    private readonly RagSettings _ragSettings;

    public ChatService(
        IChatClient chatClient,
        IEmbeddingService embeddingService,
        IQueryRewriter queryRewriter,
        IPurchaseRequisitionRepository repository,
        IRagReportWriter reportWriter,
        ILogger<ChatService> logger,
        IOptions<RagSettings> ragSettings)
    {
        _chatClient = chatClient;
        _embeddingService = embeddingService;
        _queryRewriter = queryRewriter;
        _repository = repository;
        _reportWriter = reportWriter;
        _logger = logger;
        _ragSettings = ragSettings.Value;
    }

    private static readonly System.Text.RegularExpressions.Regex SupplierCodeRegex =
        new(@"SUP\d+", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex ItemRegex =
        new(@"ITM-\d+", System.Text.RegularExpressions.RegexOptions.Compiled);

    private sealed record RetrievalContext(
        List<Domain.PurchaseRequisition> Results,
        Dictionary<string, double?> Similarities,
        string? RewrittenQuery);

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

        var retrieval = await RetrieveContextAsync(request.Question, topK, minSimilarity, cancellationToken);
        report.RewrittenQuery = retrieval.RewrittenQuery;
        report.RetrievedCount = retrieval.Results.Count;

        ChatResponse response;
        if (retrieval.Results.Count == 0)
        {
            report.UsedNoContextFallback = true;
            response = new ChatResponse { Answer = NoContextMessage, RetrievedCount = 0 };
        }
        else
        {
            var prompt = BuildPrompt(request.Question, retrieval.Results);

            var chatResponse = await _chatClient.GetResponseAsync(prompt, cancellationToken: cancellationToken);
            response = new ChatResponse
            {
                Answer = chatResponse.Text,
                RetrievedCount = retrieval.Results.Count,
            };
        }

        report.Answer = response.Answer;
        PopulateReport(report, retrieval, topK, minSimilarity);

        await WriteReportAsync(report, cancellationToken);

        return response;
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

        var retrieval = await RetrieveContextAsync(request.Question, topK, minSimilarity, cancellationToken);
        report.RewrittenQuery = retrieval.RewrittenQuery;
        report.RetrievedCount = retrieval.Results.Count;

        if (retrieval.Results.Count == 0)
        {
            report.UsedNoContextFallback = true;
            report.Answer = NoContextMessage;
            yield return NoContextMessage;

            await WriteReportAsync(report, cancellationToken);
            yield break;
        }

        var messages = BuildStreamingMessages(request, retrieval.Results);

        var answer = new StringBuilder();
        var streaming = _chatClient.GetStreamingResponseAsync(messages, cancellationToken: cancellationToken);
        await foreach (var update in streaming.WithCancellation(cancellationToken))
        {
            if (update.Text is { Length: > 0 } token)
            {
                answer.Append(token);
                yield return token;
            }
        }

        report.Answer = answer.ToString();
        PopulateReport(report, retrieval, topK, minSimilarity);

        await WriteReportAsync(report, cancellationToken);
    }

    private async Task<RetrievalContext> RetrieveContextAsync(
        string question,
        int topK,
        double minSimilarity,
        CancellationToken cancellationToken)
    {
        var items = ItemRegex.Matches(question)
            .Select(m => m.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var supplierCodes = SupplierCodeRegex.Matches(question)
            .Select(m => m.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var results = new List<Domain.PurchaseRequisition>();
        var seen = new HashSet<string>();
        var similarities = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);

        if (items.Count > 0 || supplierCodes.Count > 0)
        {
            var byCodes = await _repository.SearchByCodesAsync(items, supplierCodes, topK, cancellationToken);
            foreach (var r in byCodes)
            {
                if (seen.Add(r.PurchaseRequisitionId))
                {
                    results.Add(r);
                    similarities[r.PurchaseRequisitionId] = null;
                }
            }
        }

        string? rewrittenQuery = null;
        if (results.Count < topK)
        {
            var optimizedQuery = await _queryRewriter.RewriteAsync(question, cancellationToken);
            rewrittenQuery = optimizedQuery;

            var questionEmbedding = await _embeddingService.GenerateAsync(optimizedQuery, cancellationToken);
            var vectorResults = await _repository.SearchAsync(questionEmbedding, topK, minSimilarity, cancellationToken);
            foreach (var result in vectorResults)
            {
                var r = result.Requisition;
                if (seen.Add(r.PurchaseRequisitionId))
                {
                    results.Add(r);
                    similarities[r.PurchaseRequisitionId] = result.Similarity;
                }

                if (results.Count >= topK)
                {
                    break;
                }
            }
        }

        return new RetrievalContext(results, similarities, rewrittenQuery);
    }

    private void PopulateReport(
        RagQueryReport report,
        RetrievalContext retrieval,
        int topK,
        double minSimilarity)
    {
        foreach (var r in retrieval.Results)
        {
            report.RetrievedItems.Add(new RagRetrievedItem
            {
                PurchaseRequisitionId = r.PurchaseRequisitionId,
                SupplierCode = r.SupplierCode,
                SupplierName = r.SupplierName,
                Item = r.Item,
                ItemName = r.ItemName,
                Description = r.Description,
                Similarity = retrieval.Similarities.TryGetValue(r.PurchaseRequisitionId, out var sim) ? sim : null,
            });
        }
    }

    private List<ChatMessage> BuildStreamingMessages(ChatStreamRequest request, IReadOnlyList<Domain.PurchaseRequisition> results)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, BuildSystemPrompt(results)),
        };

        foreach (var msg in request.Messages)
        {
            var role = string.Equals(msg.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                ? ChatRole.Assistant
                : ChatRole.User;
            messages.Add(new ChatMessage(role, msg.Content));
        }

        messages.Add(new ChatMessage(ChatRole.User, request.Question));
        return messages;
    }

    private static string BuildSystemPrompt(IReadOnlyList<Domain.PurchaseRequisition> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a helpful assistant answering questions about purchase requisitions.");
        sb.AppendLine("Use only the provided context to answer. If the context does not contain the answer, say so.");
        sb.AppendLine();
        sb.AppendLine("Context (purchase requisitions):");
        sb.AppendLine();

        foreach (var r in results)
        {
            sb.AppendLine($"- {r.PurchaseRequisitionId} | Supplier Code: {r.SupplierCode} | Supplier Name: {r.SupplierName} | Item: {r.Item} | Item Name: {r.ItemName} | Description: {r.Description}");
        }

        return sb.ToString();
    }

    private static string BuildPrompt(string question, IReadOnlyList<Domain.PurchaseRequisition> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine(BuildSystemPrompt(results));
        sb.AppendLine();
        sb.AppendLine($"Question: {question}");
        return sb.ToString();
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
}
