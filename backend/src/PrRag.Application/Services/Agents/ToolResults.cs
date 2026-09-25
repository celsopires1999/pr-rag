using System.ComponentModel;
using System.Text.Json.Serialization;
using PrRag.Application.Domain;
using PrRag.Application.DTOs;

namespace PrRag.Application.Services.Agents;

/// <summary>
/// Model-facing result contracts for the agent tools. These types are the
/// JSON the chat model receives as a tool result, and are deliberately
/// separate from the RAG observability report DTOs: the report schema is
/// frozen, while this payload is free to change shape. Every property
/// carries an explicit <see cref="JsonPropertyNameAttribute"/> so the wire
/// names are camelCase regardless of the serializer's naming policy.
/// </summary>
public sealed class ToolRequisitionHit
{
    [JsonPropertyName("requisitionId")]
    [Description("The id of the purchase requisition")]
    public string RequisitionId { get; set; } = string.Empty;

    [JsonPropertyName("supplierCode")]
    [Description("The code of the supplier")]
    public string SupplierCode { get; set; } = string.Empty;

    [JsonPropertyName("supplierName")]
    [Description("The name of the supplier")]
    public string SupplierName { get; set; } = string.Empty;

    [JsonPropertyName("item")]
    [Description("The official code of the item (ITM-*)")]
    public string Item { get; set; } = string.Empty;

    [JsonPropertyName("itemName")]
    [Description("The official name of the item")]
    public string ItemName { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    [Description("The free-text description of the requisition")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("similarity")]
    [Description("Cosine similarity to the search query; null for exact code search")]
    public double? Similarity { get; set; }

    public static ToolRequisitionHit From(RagRetrievedItem item) => new()
    {
        RequisitionId = item.PurchaseRequisitionId,
        SupplierCode = item.SupplierCode,
        SupplierName = item.SupplierName,
        Item = item.Item,
        ItemName = item.ItemName,
        Description = item.Description,
        Similarity = item.Similarity,
    };
}

/// <summary>Result of an exact-code or semantic requisition search.</summary>
public sealed class ToolSearchResult
{
    [JsonPropertyName("count")]
    [Description("How many requisitions matched; zero means no matches were found")]
    public int Count { get; set; }

    [JsonPropertyName("items")]
    [Description("The matching requisitions; empty when count is zero")]
    public List<ToolRequisitionHit> Items { get; set; } = [];

    public static ToolSearchResult From(IReadOnlyCollection<RagRetrievedItem> items) => new()
    {
        Count = items.Count,
        Items = items.Select(ToolRequisitionHit.From).ToList(),
    };
}

/// <summary>A single supplier that provided an item.</summary>
public sealed class ToolSupplierHit
{
    [JsonPropertyName("supplierCode")]
    [Description("The code of the supplier")]
    public string SupplierCode { get; set; } = string.Empty;

    [JsonPropertyName("supplierName")]
    [Description("The name of the supplier")]
    public string SupplierName { get; set; } = string.Empty;

    public static ToolSupplierHit From(SupplierSummary supplier) => new()
    {
        SupplierCode = supplier.SupplierCode,
        SupplierName = supplier.SupplierName,
    };
}

/// <summary>Result of an item-to-supplier lookup; each supplier appears once.</summary>
public sealed class ToolSupplierList
{
    [JsonPropertyName("count")]
    [Description("How many distinct suppliers were found; zero means the item has no recorded supplier")]
    public int Count { get; set; }

    [JsonPropertyName("suppliers")]
    [Description("The distinct suppliers for the item; empty when count is zero")]
    public List<ToolSupplierHit> Suppliers { get; set; } = [];

    public static ToolSupplierList From(IReadOnlyCollection<SupplierSummary> suppliers) => new()
    {
        Count = suppliers.Count,
        Suppliers = suppliers.Select(ToolSupplierHit.From).ToList(),
    };
}

/// <summary>Result of a skill activation attempt.</summary>
public sealed class ToolSkillActivation
{
    [JsonPropertyName("name")]
    [Description("The skill name that was requested")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("found")]
    [Description("Whether a skill with that name was found; when false, follow the message instead of the body")]
    public bool Found { get; set; }

    [JsonPropertyName("body")]
    [Description("The skill's instructions to follow; null when the skill was not found")]
    public string? Body { get; set; }

    [JsonPropertyName("message")]
    [Description("Human-readable outcome: the guidance to follow, or the reason activation failed")]
    public string Message { get; set; } = string.Empty;

    public static ToolSkillActivation Activated(string name, string body) => new()
    {
        Name = name,
        Found = true,
        Body = body,
        Message = body,
    };

    public static ToolSkillActivation NotFound(string name, string message) => new()
    {
        Name = name,
        Found = false,
        Message = message,
    };
}

/// <summary>Result of staging an unpersisted requisition draft.</summary>
public sealed class ToolDraftStaged
{
    [JsonPropertyName("staged")]
    [Description("Whether the draft is now staged in the session; when false, nothing was stored and nothing was presented")]
    public bool Staged { get; set; }

    [JsonPropertyName("draft")]
    [Description("The staged field values; null when staging failed")]
    public ToolDraftFields? Draft { get; set; }

    [JsonPropertyName("message")]
    [Description("Human-readable outcome: what to present to the user and ask for, or why staging failed")]
    public string Message { get; set; } = string.Empty;

    public static ToolDraftStaged Ok(ToolDraftFields fields, string message) => new()
    {
        Staged = true,
        Draft = fields,
        Message = message,
    };

    public static ToolDraftStaged Rejected(string message) => new()
    {
        Staged = false,
        Message = message,
    };
}

/// <summary>The six requisition field values carried by a staged draft.</summary>
public sealed class ToolDraftFields
{
    [JsonPropertyName("supplierCode")]
    [Description("The code of the supplier")]
    public string SupplierCode { get; set; } = string.Empty;

    [JsonPropertyName("item")]
    [Description("The code of the item")]
    public string Item { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    [Description("The description of the requisition")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("quantity")]
    [Description("The quantity of the item")]
    public decimal Quantity { get; set; }

    [JsonPropertyName("date")]
    [Description("The date of the requisition in ISO format yyyy-MM-dd")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("requester")]
    [Description("The requester of the requisition")]
    public string Requester { get; set; } = string.Empty;

    public static ToolDraftFields From(RequisitionDraft draft) => new()
    {
        SupplierCode = draft.SupplierCode,
        Item = draft.Item,
        Description = draft.Description,
        Quantity = draft.Quantity,
        Date = draft.Date,
        Requester = draft.Requester,
    };
}

/// <summary>Result of an attempt to record the user's confirmation of a draft.</summary>
public sealed class ToolDraftConfirmation
{
    [JsonPropertyName("confirmed")]
    [Description("Whether the draft is now confirmed and may be persisted by create_requisition")]
    public bool Confirmed { get; set; }

    [JsonPropertyName("draft")]
    [Description("The confirmed field values that create_requisition will persist; null when not confirmed")]
    public ToolDraftFields? Draft { get; set; }

    [JsonPropertyName("message")]
    [Description("Human-readable outcome: confirmation recorded, declined, or the reason it could not be recorded")]
    public string Message { get; set; } = string.Empty;

    public static ToolDraftConfirmation Recorded(ToolDraftFields fields, string message) => new()
    {
        Confirmed = true,
        Draft = fields,
        Message = message,
    };

    public static ToolDraftConfirmation Declined(string message) => new()
    {
        Confirmed = false,
        Message = message,
    };
}

/// <summary>Result of a purchase-requisition creation attempt.</summary>
public sealed class ToolRequisitionWrite
{
    [JsonPropertyName("success")]
    [Description("Whether the requisition was persisted; when false, nothing was written and the message says why")]
    public bool Success { get; set; }

    [JsonPropertyName("requisitionId")]
    [Description("The id of the created requisition; null when the write was refused")]
    public string? RequisitionId { get; set; }

    [JsonPropertyName("missingStep")]
    [Description("The prior step the model must take before retrying (for example create_requisition_draft or confirm_requisition_draft); null when the refusal is not a missing-step problem")]
    public string? MissingStep { get; set; }

    [JsonPropertyName("conflictingField")]
    [Description("The argument that disagrees with the confirmed draft; null unless the arguments contradict the draft")]
    public string? ConflictingField { get; set; }

    [JsonPropertyName("expectedValue")]
    [Description("The confirmed draft value for conflictingField; null unless the arguments contradict the draft")]
    public string? ExpectedValue { get; set; }

    [JsonPropertyName("message")]
    [Description("Human-readable outcome describing what happened and what to do next")]
    public string Message { get; set; } = string.Empty;

    public static ToolRequisitionWrite Created(string requisitionId) => new()
    {
        Success = true,
        RequisitionId = requisitionId,
        Message = $"Requisition created: {requisitionId}.",
    };

    public static ToolRequisitionWrite Rejected(string message) => new()
    {
        Success = false,
        Message = message,
    };

    /// <summary>
    /// Refusal naming the prior step the model skipped. Structured rather than
    /// prose-only so a model that ignored the tool description can still
    /// recover by reading the missing step.
    /// </summary>
    public static ToolRequisitionWrite RefusedForMissingStep(string missingStep, string message) => new()
    {
        Success = false,
        MissingStep = missingStep,
        Message = message,
    };

    /// <summary>
    /// Refusal naming the argument that contradicts the confirmed draft, with
    /// the confirmed value, so the model can re-draft instead of retrying blind.
    /// </summary>
    public static ToolRequisitionWrite RefusedForConflict(string field, string expected, string message) => new()
    {
        Success = false,
        ConflictingField = field,
        ExpectedValue = expected,
        Message = message,
    };
}
