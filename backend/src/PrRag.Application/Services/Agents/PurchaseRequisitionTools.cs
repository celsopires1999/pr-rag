using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using PrRag.Application.Abstractions;
using PrRag.Application.Domain;
using PrRag.Application.DTOs;

namespace PrRag.Application.Services.Agents;

/// <summary>
/// The seven purchase-requisition tools exposed to the agent. Each handler is a
/// <c>[Description]</c>-annotated method registered directly via
/// <c>AIFunctionFactory</c> under its <c>*Tool</c> wire name, so the
/// descriptions on the methods and their parameters reach the JSON schema the
/// model sees. Per-turn retrieval parameters and bookkeeping come from the
/// scoped <see cref="AgentTurnContext"/>.
///
/// Creation is a three-step flow — <c>create_requisition_draft</c> stages an
/// unpersisted draft, <c>confirm_requisition_draft</c> records the user's
/// explicit yes, and only then does <c>create_requisition</c> persist. The
/// gate lives in <see cref="CreateRequisitionAsync"/> rather than in prompt
/// text, because the model does not reliably follow advisory instructions.
/// </summary>
public sealed class PurchaseRequisitionTools
{
    public const string SearchByCodesTool = "search_by_codes";

    public const string SearchSemanticTool = "search_semantic";

    public const string ActivateSkillTool = "activate_skill";

    public const string GetSuppliersByItemTool = "get_suppliers_by_item";

    public const string CreateRequisitionDraftTool = "create_requisition_draft";

    public const string ConfirmRequisitionDraftTool = "confirm_requisition_draft";

    public const string CreateRequisitionTool = "create_requisition";

    private readonly IEmbeddingService _embeddingService;
    private readonly IPurchaseRequisitionRepository _repository;
    private readonly ISkillService _skillService;
    private readonly IRequisitionWriter _requisitionWriter;
    private readonly AgentTurnContext _turnContext;
    private readonly ILogger<PurchaseRequisitionTools> _logger;
    private readonly List<AITool> _tools = [];

    private static readonly string[] CreateRequisitionArguments =
        ["supplierCode", "item", "description", "quantity", "date", "requester"];

    public PurchaseRequisitionTools(
        IEmbeddingService embeddingService,
        IPurchaseRequisitionRepository repository,
        ISkillService skillService,
        IRequisitionWriter requisitionWriter,
        AgentTurnContext turnContext,
        ILogger<PurchaseRequisitionTools> logger)
    {
        _embeddingService = embeddingService;
        _repository = repository;
        _skillService = skillService;
        _requisitionWriter = requisitionWriter;
        _turnContext = turnContext;
        _logger = logger;

        // Register the annotated methods themselves, not forwarding lambdas:
        // AIFunctionFactory derives the parameter schema from the delegate's
        // MethodInfo, so a lambda would drop every parameter [Description].
        RegisterFunction(SearchByCodesTool, SearchByCodesAsync);
        RegisterFunction(SearchSemanticTool, SearchSemanticAsync);
        RegisterFunction(ActivateSkillTool, ActivateSkillAsync);
        RegisterFunction(GetSuppliersByItemTool, GetSuppliersByItemAsync);
        RegisterFunction(CreateRequisitionDraftTool, CreateRequisitionDraftAsync);
        RegisterFunction(ConfirmRequisitionDraftTool, ConfirmRequisitionDraftAsync);
        RegisterFunction(CreateRequisitionTool, CreateRequisitionAsync);
    }

    /// <summary>The fixed tool list bound to the agent.</summary>
    public IList<AITool> All => _tools;

    private void RegisterFunction(string wireName, Delegate handler)
    {
        // No Description override: AIFunctionFactory reads the [Description]
        // attribute on the method, keeping one source of truth.
        _tools.Add(AIFunctionFactory.Create(handler, new AIFunctionFactoryOptions { Name = wireName }));
    }

    private void RecordToolCall(string name, IDictionary<string, object?> arguments)
    {
        _turnContext.ToolCalls.Add(new RagToolCall
        {
            Name = name,
            Arguments = new Dictionary<string, object?>(arguments),
        });
    }

    private void LogResult(string name, string[] argumentNames, long startedAt, int resultCount) =>
        _logger.LogInformation(
            "Tool {ToolName} invoked with {Arguments} returned {ResultCount} item(s) in {ElapsedMs}ms",
            name,
            argumentNames,
            resultCount,
            (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

    [Description(
        "Search purchase requisitions by exact item codes (ITM-*) and/or supplier codes (SUP*). " +
        "Returns matching requisitions with their supplier and item details.")]
    private async Task<ToolSearchResult> SearchByCodesAsync(
        [Description("The item codes to search for")] IReadOnlyList<string>? items = null,
        [Description("The supplier codes to search for")] IReadOnlyList<string>? suppliers = null,
        CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        RecordToolCall(SearchByCodesTool, new Dictionary<string, object?>
        {
            ["items"] = items,
            ["suppliers"] = suppliers,
        });

        var results = await _repository.SearchByCodesAsync(items, suppliers, _turnContext.TopK, cancellationToken);
        var mapped = results.Select(r => RagRetrievedItem.From(r, null)).ToList();
        _turnContext.RetrievedItems.AddRange(mapped);

        LogResult(SearchByCodesTool, ["items", "suppliers"], startedAt, mapped.Count);
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
        RecordToolCall(SearchSemanticTool, new Dictionary<string, object?>
        {
            ["query"] = query,
        });

        _turnContext.RewrittenQuery = query;

        var embedding = await _embeddingService.GenerateAsync(query, cancellationToken);
        var results = await _repository.SearchAsync(embedding, _turnContext.TopK, _turnContext.MinSimilarity, cancellationToken);
        var mapped = results.Select(r => RagRetrievedItem.From(r.Requisition, r.Similarity)).ToList();
        _turnContext.RetrievedItems.AddRange(mapped);

        LogResult(SearchSemanticTool, ["query"], startedAt, mapped.Count);
        return ToolSearchResult.From(mapped);
    }

    [Description(
        "Activates a skill by name to guide the conversation. Use it when the user's request matches the intent of " +
        "one of the available skills listed in the Skills section. Returns the skill's instructions to follow. " +
        "Unknown skills return an error listing the available skills.")]
    private async Task<ToolSkillActivation> ActivateSkillAsync(
        [Description("The name of the skill to activate")] string name,
        CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        RecordToolCall(ActivateSkillTool, new Dictionary<string, object?>
        {
            ["name"] = name,
        });

        var skill = await _skillService.GetSkillAsync(name, cancellationToken);
        if (skill is null)
        {
            var available = _skillService.GetManifest();
            var names = available.Count == 0 ? "none" : string.Join(", ", available.Select(s => s.Name));
            var message = $"Unknown skill '{name}'. Available skills: {names}.";
            LogResult(ActivateSkillTool, ["name"], startedAt, 0);
            return ToolSkillActivation.NotFound(name, message);
        }

        SkillSessionState.Activate(_turnContext.Session!, skill.Name, skill.Body);
        LogResult(ActivateSkillTool, ["name"], startedAt, 1);
        return ToolSkillActivation.Activated(skill.Name, skill.Body);
    }

    [Description(
        "Stages a new purchase requisition as an unconfirmed draft in this session. Nothing is written to the " +
        "database by this tool. Call it once you have collected all six fields from the user. It then returns the " +
        "draft for you to present back to the user as a summary and ask for explicit confirmation. " +
        "Re-calling it replaces the previous draft and discards any earlier confirmation. " +
        "Required parameters: supplierCode, item, description, quantity (a positive number), date (ISO format yyyy-MM-dd), requester.")]
    private Task<ToolDraftStaged> CreateRequisitionDraftAsync(
        [Description("The code of the supplier")] string supplierCode,
        [Description("The code of the item")] string item,
        [Description("The description of the requisition")] string description,
        [Description("The quantity of the item")] decimal quantity,
        [Description("The date of the requisition")] string date,
        [Description("The requester of the requisition")] string requester,
        CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        RecordToolCall(CreateRequisitionDraftTool, new Dictionary<string, object?>
        {
            ["supplierCode"] = supplierCode,
            ["item"] = item,
            ["description"] = description,
            ["quantity"] = quantity,
            ["date"] = date,
            ["requester"] = requester,
        });

        var draft = new RequisitionDraft(supplierCode, item, description, quantity, date, requester);

        // Reuse the persistence DTO's validation so staging and writing agree
        // on what "valid" means.
        var validationError = NewPurchaseRequisition.From(draft).Validate();
        if (validationError is not null)
        {
            LogResult(CreateRequisitionDraftTool, CreateRequisitionArguments, startedAt, 0);
            return Task.FromResult(ToolDraftStaged.Rejected(validationError));
        }

        RequisitionDraftSessionState.Stage(_turnContext.Session!, draft);
        _turnContext.DraftStaged = true;
        _turnContext.DraftPresented = true;

        LogResult(CreateRequisitionDraftTool, CreateRequisitionArguments, startedAt, 1);
        return Task.FromResult(ToolDraftStaged.Ok(
            ToolDraftFields.From(draft),
            "Draft staged. Present these exact values back to the user as a summary and ask for explicit confirmation; " +
            "do not call create_requisition yet."));
    }

    [Description(
        "Records that the user explicitly confirmed the staged requisition draft. Call it ONLY after the user has " +
        "answered yes to the draft summary you presented — pass the user's answer as 'yes'. A decline leaves the " +
        "draft open for revision. Only a recorded confirmation unlocks create_requisition.")]
    private Task<ToolDraftConfirmation> ConfirmRequisitionDraftAsync(
        [Description("The user's answer to the draft confirmation, for example yes or no")] string answer,
        CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        RecordToolCall(ConfirmRequisitionDraftTool, new Dictionary<string, object?>
        {
            ["answer"] = answer,
        });

        var session = _turnContext.Session!;
        var snapshot = RequisitionDraftSessionState.Read(session);

        if (snapshot.Draft is null)
        {
            LogResult(ConfirmRequisitionDraftTool, ["answer"], startedAt, 0);
            return Task.FromResult(ToolDraftConfirmation.Declined(
                "There is no staged requisition draft in this session. Call create_requisition_draft with the six " +
                "fields first, then ask the user to confirm."));
        }

        if (!snapshot.Presented)
        {
            LogResult(ConfirmRequisitionDraftTool, ["answer"], startedAt, 0);
            return Task.FromResult(ToolDraftConfirmation.Declined(
                "The draft has not been presented to the user yet. Present the draft as a summary and ask for " +
                "confirmation before recording it."));
        }

        if (!IsAffirmative(answer))
        {
            LogResult(ConfirmRequisitionDraftTool, ["answer"], startedAt, 0);
            return Task.FromResult(ToolDraftConfirmation.Declined(
                $"'{answer}' is not an explicit confirmation, so the draft stays unconfirmed. Keep the draft open and " +
                "ask the user what to change, or whether they want to proceed."));
        }

        if (!RequisitionDraftSessionState.MarkConfirmed(session))
        {
            LogResult(ConfirmRequisitionDraftTool, ["answer"], startedAt, 0);
            return Task.FromResult(ToolDraftConfirmation.Declined(
                "The draft could not be confirmed. Present the draft again and ask the user to confirm."));
        }

        var confirmed = RequisitionDraftSessionState.Read(session).Draft!;
        _turnContext.DraftConfirmed = true;
        LogResult(ConfirmRequisitionDraftTool, ["answer"], startedAt, 1);
        return Task.FromResult(ToolDraftConfirmation.Recorded(
            ToolDraftFields.From(confirmed),
            "Confirmation recorded. Call create_requisition with exactly these values to persist the requisition."));
    }

    [Description(
        "Persists a new purchase requisition in the database. It REQUIRES a draft the user already confirmed: first " +
        "call create_requisition_draft, present the draft and ask the user to confirm, then call " +
        "confirm_requisition_draft with their yes, and only then call this tool with the same six values. " +
        "Calling it without a confirmed draft is refused and nothing is written. " +
        "Required parameters: supplierCode, item, description, quantity (a positive number), date (ISO format yyyy-MM-dd), requester. " +
        "Refuses to create a requisition when no existing requisition has the same item + supplier combination.")]
    private async Task<ToolRequisitionWrite> CreateRequisitionAsync(
        [Description("The code of the supplier")] string supplierCode,
        [Description("The code of the item")] string item,
        [Description("The description of the requisition")] string description,
        [Description("The quantity of the item")] decimal quantity,
        [Description("The date of the requisition")] string date,
        [Description("The requester of the requisition")] string requester,
        CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        RecordToolCall(CreateRequisitionTool, new Dictionary<string, object?>
        {
            ["supplierCode"] = supplierCode,
            ["item"] = item,
            ["description"] = description,
            ["quantity"] = quantity,
            ["date"] = date,
            ["requester"] = requester,
        });

        var session = _turnContext.Session!;
        var snapshot = RequisitionDraftSessionState.Read(session);

        // Gate: no confirmed draft, no requisition. This is enforced here rather
        // than left to the model because the confirmation procedure is advisory
        // text and the model demonstrably skips it.
        if (!snapshot.HasConfirmedDraft)
        {
            var missingStep = snapshot.Draft is null
                ? CreateRequisitionDraftTool
                : ConfirmRequisitionDraftTool;
            var detail = snapshot.Draft is null
                ? "No requisition draft is staged in this session."
                : "A draft is staged but the user has not confirmed it.";

            LogResult(CreateRequisitionTool, CreateRequisitionArguments, startedAt, 0);
            return ToolRequisitionWrite.RefusedForMissingStep(
                missingStep,
                $"Cannot create requisition: {detail} Call {CreateRequisitionDraftTool} to stage the draft, present it, " +
                $"then {ConfirmRequisitionDraftTool} to record the user's explicit yes, before calling " +
                $"{CreateRequisitionTool}. Nothing was created.");
        }

        var draft = snapshot.Draft!;
        var conflict = FindConflict(draft, supplierCode, item, description, quantity, date, requester);
        if (conflict is not null)
        {
            LogResult(CreateRequisitionTool, CreateRequisitionArguments, startedAt, 0);
            return ToolRequisitionWrite.RefusedForConflict(
                conflict.Value.Field,
                conflict.Value.Expected,
                $"Cannot create requisition: the argument '{conflict.Value.Field}' does not match the confirmed " +
                $"draft, whose value is '{conflict.Value.Expected}'. Call {CreateRequisitionDraftTool} with the " +
                $"corrected values, present it, and have the user confirm again. Nothing was created.");
        }

        var requisition = NewPurchaseRequisition.From(draft);

        var validationError = requisition.Validate();
        if (validationError is not null)
        {
            LogResult(CreateRequisitionTool, CreateRequisitionArguments, startedAt, 0);
            return ToolRequisitionWrite.Rejected(validationError);
        }

        var combinationExists = await _repository.ExistsItemSupplierCombinationAsync(
            requisition.Item,
            requisition.SupplierCode,
            cancellationToken);

        if (!combinationExists)
        {
            LogResult(CreateRequisitionTool, CreateRequisitionArguments, startedAt, 0);
            return ToolRequisitionWrite.Rejected(
                $"Cannot create requisition: supplier {requisition.SupplierCode} has no recorded requisition for item {requisition.Item}. " +
                "The supplier is not registered for that item, so no requisition was created. " +
                "Ask the user to confirm the item and supplier.");
        }

        var result = await _requisitionWriter.WriteAsync(
            requisition,
            _turnContext.SessionId,
            cancellationToken);

        LogResult(CreateRequisitionTool, CreateRequisitionArguments, startedAt, result.Success ? 1 : 0);

        if (result.Success)
        {
            SkillSessionState.Clear(session);
            RequisitionDraftSessionState.Clear(session);
            _turnContext.RequisitionPersisted = true;
            return ToolRequisitionWrite.Created(result.RequisitionId!);
        }

        return ToolRequisitionWrite.Rejected(result.Error!);
    }

    private static bool IsAffirmative(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return false;
        }

        var normalized = answer.Trim().TrimEnd('!', '.', ' ').ToLowerInvariant();
        return normalized is "yes" or "y" or "yeah" or "yep" or "yes please" or "confirm" or "confirmed"
            or "approve" or "approved" or "go ahead" or "proceed";
    }

    /// <summary>
    /// Returns the first argument that disagrees with the confirmed draft, or
    /// null when the arguments match it exactly.
    /// </summary>
    private static (string Field, string Expected)? FindConflict(
        RequisitionDraft draft,
        string supplierCode,
        string item,
        string description,
        decimal quantity,
        string date,
        string requester)
    {
        if (!string.Equals(draft.SupplierCode, supplierCode, StringComparison.Ordinal))
        {
            return ("supplierCode", draft.SupplierCode);
        }

        if (!string.Equals(draft.Item, item, StringComparison.Ordinal))
        {
            return ("item", draft.Item);
        }

        if (!string.Equals(draft.Description, description, StringComparison.Ordinal))
        {
            return ("description", draft.Description);
        }

        if (draft.Quantity != quantity)
        {
            return ("quantity", draft.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (!string.Equals(draft.Date, date, StringComparison.Ordinal))
        {
            return ("date", draft.Date);
        }

        if (!string.Equals(draft.Requester, requester, StringComparison.Ordinal))
        {
            return ("requester", draft.Requester);
        }

        return null;
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
        RecordToolCall(GetSuppliersByItemTool, new Dictionary<string, object?>
        {
            ["item"] = item,
        });

        var suppliers = await _repository.GetSuppliersByItemAsync(item, cancellationToken);

        LogResult(GetSuppliersByItemTool, ["item"], startedAt, suppliers.Count);
        return ToolSupplierList.From(suppliers);
    }

}
