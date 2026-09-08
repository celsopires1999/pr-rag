using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PrRag.Application.Abstractions;
using PrRag.Application.DTOs;

namespace PrRag.Application.Services.Agents;

/// <summary>
/// The four purchase-requisition tools exposed to the agent. Each handler is an
/// <c>[Description]</c>-annotated method; <see cref="All"/> binds them via
/// <c>AIFunctionFactory</c> under their wire-level names. Per-turn retrieval
/// parameters and bookkeeping come from the scoped <see cref="AgentTurnContext"/>.
/// </summary>
public sealed class PurchaseRequisitionTools
{
    private const string SearchByCodesDescription =
        "Search purchase requisitions by exact item codes (ITM-*) and/or supplier codes (SUP*). " +
        "Returns matching requisitions with their supplier and item details.";

    private const string SearchSemanticDescription =
        "Search purchase requisitions by semantic similarity to the given query text, in any language. " +
        "Returns the most relevant requisitions.";

    private const string ActivateSkillDescription =
        "Activates a skill by name to guide the conversation. Use it when the user's request matches the intent of " +
        "one of the available skills listed in the Skills section. Returns the skill's instructions to follow. " +
        "Unknown skills return an error listing the available skills.";

    private const string CreateRequisitionDescription =
        "Persists a new purchase requisition in the database. Call it ONLY after the user has explicitly confirmed " +
        "the drafted requisition; never invent field values — use exactly the values the user provided and validated. " +
        "Required parameters: supplierCode, item, description, quantity (a positive number), date (ISO format yyyy-MM-dd), requester. " +
        "Refuses to create a requisition when no existing requisition has the same item + supplier combination.";

    private readonly IEmbeddingService _embeddingService;
    private readonly IPurchaseRequisitionRepository _repository;
    private readonly ISkillService _skillService;
    private readonly IRequisitionWriter _requisitionWriter;
    private readonly AgentTurnContext _turnContext;
    private readonly IList<AITool> _tools;

    public PurchaseRequisitionTools(
        IEmbeddingService embeddingService,
        IPurchaseRequisitionRepository repository,
        ISkillService skillService,
        IRequisitionWriter requisitionWriter,
        AgentTurnContext turnContext)
    {
        _embeddingService = embeddingService;
        _repository = repository;
        _skillService = skillService;
        _requisitionWriter = requisitionWriter;
        _turnContext = turnContext;

        _tools = new List<AITool>();
        RegisterFunction(
            "search_by_codes",
            SearchByCodesDescription,
            (IReadOnlyList<string>? items = null, IReadOnlyList<string>? suppliers = null, CancellationToken ct = default) =>
                SearchByCodesAsync(items, suppliers, ct));
        RegisterFunction(
            "search_semantic",
            SearchSemanticDescription,
            (string query, CancellationToken ct) => SearchSemanticAsync(query, ct));
        RegisterFunction(
            "activate_skill",
            ActivateSkillDescription,
            (string name, CancellationToken ct) => ActivateSkillAsync(name, ct));
        RegisterFunction(
            "create_requisition",
            CreateRequisitionDescription,
            (string supplierCode, string item, string description, decimal quantity, string date, string requester, CancellationToken ct) =>
                CreateRequisitionAsync(supplierCode, item, description, quantity, date, requester, ct));
    }

    /// <summary>The fixed tool list bound to the agent.</summary>
    public IList<AITool> All => _tools;

    private void RegisterFunction(string name, string description, Delegate handler)
    {
        var function = AIFunctionFactory.Create(handler, new AIFunctionFactoryOptions
        {
            Name = name,
            Description = description,
            MarshalResult = (result, _, _) => new ValueTask<object?>(result),
        });
        _tools.Add(function);
    }

    [Description(SearchByCodesDescription)]
    private async Task<IReadOnlyList<RagRetrievedItem>> SearchByCodesAsync(
        IReadOnlyList<string>? items,
        IReadOnlyList<string>? suppliers,
        CancellationToken cancellationToken)
    {
        var results = await _repository.SearchByCodesAsync(items, suppliers, _turnContext.TopK, cancellationToken);
        var mapped = results.Select(r => RagRetrievedItem.From(r, null)).ToList();
        _turnContext.RetrievedItems.AddRange(mapped);
        return mapped;
    }

    [Description(SearchSemanticDescription)]
    private async Task<IReadOnlyList<RagRetrievedItem>> SearchSemanticAsync(
        string query,
        CancellationToken cancellationToken)
    {
        _turnContext.RewrittenQuery = query;

        var embedding = await _embeddingService.GenerateAsync(query, cancellationToken);
        var results = await _repository.SearchAsync(embedding, _turnContext.TopK, _turnContext.MinSimilarity, cancellationToken);
        var mapped = results.Select(r => RagRetrievedItem.From(r.Requisition, r.Similarity)).ToList();
        _turnContext.RetrievedItems.AddRange(mapped);
        return mapped;
    }

    [Description(ActivateSkillDescription)]
    private async Task<string> ActivateSkillAsync(string name, CancellationToken cancellationToken)
    {
        var skill = await _skillService.GetSkillAsync(name, cancellationToken);
        if (skill is null)
        {
            var available = _skillService.GetManifest();
            var names = available.Count == 0 ? "none" : string.Join(", ", available.Select(s => s.Name));
            return $"Unknown skill '{name}'. Available skills: {names}.";
        }

        SkillSessionState.Activate(_turnContext.Session!, skill.Name, skill.Body);
        return skill.Body;
    }

    [Description(CreateRequisitionDescription)]
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
            SkillSessionState.Clear(_turnContext.Session!);
            return $"Requisition created: {result.RequisitionId}.";
        }

        return result.Error!;
    }
}