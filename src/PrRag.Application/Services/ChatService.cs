using System.Text;
using Microsoft.Extensions.AI;
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
    private readonly IPurchaseRequisitionRepository _repository;
    private readonly RagSettings _ragSettings;

    public ChatService(
        IChatClient chatClient,
        IEmbeddingService embeddingService,
        IPurchaseRequisitionRepository repository,
        IOptions<RagSettings> ragSettings)
    {
        _chatClient = chatClient;
        _embeddingService = embeddingService;
        _repository = repository;
        _ragSettings = ragSettings.Value;
    }

    private static readonly System.Text.RegularExpressions.Regex SupplierCodeRegex =
        new(@"SUP\d+", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex ItemRegex =
        new(@"ITM-\d+", System.Text.RegularExpressions.RegexOptions.Compiled);

    public async Task<ChatResponse> AnswerAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        var topK = request.TopK > 0 ? request.TopK : _ragSettings.TopK;
        var minSimilarity = request.MinSimilarity > 0 ? request.MinSimilarity : _ragSettings.MinSimilarity;

        var items = ItemRegex.Matches(request.Question)
            .Select(m => m.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var supplierCodes = SupplierCodeRegex.Matches(request.Question)
            .Select(m => m.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var results = new List<Domain.PurchaseRequisition>();
        var seen = new HashSet<string>();

        if (items.Count > 0 || supplierCodes.Count > 0)
        {
            var byCodes = await _repository.SearchByCodesAsync(items, supplierCodes, topK, cancellationToken);
            foreach (var r in byCodes)
            {
                if (seen.Add(r.PurchaseRequisitionId))
                {
                    results.Add(r);
                }
            }
        }

        if (results.Count < topK)
        {
            var questionEmbedding = await _embeddingService.GenerateAsync(request.Question, cancellationToken);
            var vectorResults = await _repository.SearchAsync(questionEmbedding, topK, minSimilarity, cancellationToken);
            foreach (var r in vectorResults)
            {
                if (seen.Add(r.PurchaseRequisitionId))
                {
                    results.Add(r);
                }

                if (results.Count >= topK)
                {
                    break;
                }
            }
        }

        if (results.Count == 0)
        {
            return new ChatResponse { Answer = NoContextMessage, RetrievedCount = 0 };
        }

        var prompt = BuildPrompt(request.Question, results);

        var response = await _chatClient.GetResponseAsync(prompt, cancellationToken: cancellationToken);

        return new ChatResponse
        {
            Answer = response.Text,
            RetrievedCount = results.Count,
        };
    }

    private static string BuildPrompt(string question, IReadOnlyList<Domain.PurchaseRequisition> results)
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

        sb.AppendLine();
        sb.AppendLine($"Question: {question}");
        return sb.ToString();
    }
}
