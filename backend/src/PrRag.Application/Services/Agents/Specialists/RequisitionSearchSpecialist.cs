using System.ComponentModel;
using Microsoft.Extensions.AI;
using System.Diagnostics;
using PrRag.Application.Abstractions;
using PrRag.Application.DTOs;

namespace PrRag.Application.Services.Agents.Specialists;

/// <summary>
/// Retrieval capability: the three read-only search tools
/// (<c>search_by_codes</c>, <c>search_semantic</c>, <c>get_suppliers_by_item</c>).
///
/// Owns nothing that mutates state beyond the turn's retrieval bookkeeping, so
/// it can be delegated to in Phase 2 without any risk of a partial write.
/// </summary>
public sealed class RequisitionSearchSpecialist
{
    public const string Id = "purchase-requisition-retrieval-specialist";

    public const string DisplayName = "Purchase-requisition retrieval";

    /// <summary>
    /// The action bullets for this unit's tools, moved verbatim out of
    /// <see cref="AgentInstructions.CoreInstructions"/>. Kept beside the handlers
    /// so the prose and the code it describes cannot drift apart.
    /// </summary>
    public const string ActionBlock =
        """
        * **`search_by_codes`**: Use when the user provides exact ITM-* item codes or SUP* supplier codes. Returns basic requisition details (e.g., descriptions). *Note: Does NOT return quantity or date information.*
        * **`search_semantic`**: Use when the user asks about requisitions by meaning, general description, or keywords. Before calling, rewrite the user's question into a short, keyword-rich English query optimized for cosine similarity search. Resolve conversational references (e.g., "that one", "as seen earlier") using conversation history.
        * **`get_suppliers_by_item`**: Use when the user asks which suppliers provided, supplied, or sell a specific item (e.g., "What are the suppliers that provided the item ITM-00000000000000000008?"). Extract the item code (ITM-*) from the question and call it. If the user gives only the item name, resolve it to the code first via `search_semantic`. Returns the distinct SupplierCode + SupplierName list — echo it without inventing entries.
        """;

    private readonly IEmbeddingService _embeddingService;
    private readonly IPurchaseRequisitionRepository _repository;
    private readonly AgentTurnContext _turnContext;
    private readonly SpecialistToolSet _tools;
    private readonly List<AITool> _ownedTools = [];

    public RequisitionSearchSpecialist(
        IEmbeddingService embeddingService,
        IPurchaseRequisitionRepository repository,
        AgentTurnContext turnContext,
        SpecialistToolSet tools)
    {
        _embeddingService = embeddingService;
        _repository = repository;
        _turnContext = turnContext;
        _tools = tools;

        _ownedTools.Add(tools.Add(ToolNames.SearchByCodes, SearchByCodesAsync));
        _ownedTools.Add(tools.Add(ToolNames.SearchSemantic, SearchSemanticAsync));
        _ownedTools.Add(tools.Add(ToolNames.GetSuppliersByItem, GetSuppliersByItemAsync));

        Definition = new SpecialistDefinition(Id, DisplayName, ActionBlock, _ownedTools);
    }

    /// <summary>This unit's prose and tools, as one value.</summary>
    public SpecialistDefinition Definition { get; }

    [Description(
        "Search purchase requisitions by exact item codes (ITM-*) and/or supplier codes (SUP*). " +
        "Returns matching requisitions with their supplier and item details.")]
    private async Task<ToolSearchResult> SearchByCodesAsync(
        [Description("The item codes to search for")] IReadOnlyList<string>? items = null,
        [Description("The supplier codes to search for")] IReadOnlyList<string>? suppliers = null,
        CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        _tools.Record(ToolNames.SearchByCodes, new Dictionary<string, object?>
        {
            ["items"] = items,
            ["suppliers"] = suppliers,
        });

        var results = await _repository.SearchByCodesAsync(items, suppliers, _turnContext.TopK, cancellationToken);
        var mapped = results.Select(r => RagRetrievedItem.From(r, null)).ToList();
        _turnContext.RetrievedItems.AddRange(mapped);

        _tools.Log(ToolNames.SearchByCodes, ["items", "suppliers"], startedAt, mapped.Count);
        return ToolSearchResult.From(mapped);
    }

    [Description(
        "Search purchase requisitions by semantic similarity to the given query text, in any language. " +
        "Returns the most relevant requisitions.")]
    private async Task<ToolSearchResult> SearchSemanticAsync(
        [Description("The query to search for")] string query,
        CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        _tools.Record(ToolNames.SearchSemantic, new Dictionary<string, object?>
        {
            ["query"] = query,
        });

        _turnContext.RewrittenQuery = query;

        var embedding = await _embeddingService.GenerateAsync(query, cancellationToken);
        var results = await _repository.SearchAsync(embedding, _turnContext.TopK, _turnContext.MinSimilarity, cancellationToken);
        var mapped = results.Select(r => RagRetrievedItem.From(r.Requisition, r.Similarity)).ToList();
        _turnContext.RetrievedItems.AddRange(mapped);

        _tools.Log(ToolNames.SearchSemantic, ["query"], startedAt, mapped.Count);
        return ToolSearchResult.From(mapped);
    }

    [Description(
        "Returns the distinct list of suppliers (SupplierCode + SupplierName) that supplied the given item. " +
        "Use it when the user asks which suppliers provided, supplied, or sell a specific item. Pass the item code " +
        "(ITM-*) extracted from the question; resolve an item name to its code first via search_by_codes when needed. " +
        "Each supplier appears exactly once.")]
    private async Task<ToolSupplierList> GetSuppliersByItemAsync(
        [Description("The item code (ITM-*) to look up suppliers for")] string item,
        CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        _tools.Record(ToolNames.GetSuppliersByItem, new Dictionary<string, object?>
        {
            ["item"] = item,
        });

        var suppliers = await _repository.GetSuppliersByItemAsync(item, cancellationToken);

        _tools.Log(ToolNames.GetSuppliersByItem, ["item"], startedAt, suppliers.Count);
        return ToolSupplierList.From(suppliers);
    }
}
