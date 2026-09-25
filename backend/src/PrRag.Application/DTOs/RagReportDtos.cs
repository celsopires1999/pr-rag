using PrRag.Application.Domain;

namespace PrRag.Application.DTOs;

public readonly record struct RequisitionSearchResult(
    Domain.PurchaseRequisition Requisition,
    double? Similarity);

public sealed class RagQueryReport
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public string Question { get; set; } = string.Empty;

    public int TopK { get; set; }

    public double MinSimilarity { get; set; }

    public bool TopKFromRequest { get; set; }

    public bool MinSimilarityFromRequest { get; set; }

    public string? RewrittenQuery { get; set; }

    public string? SkillId { get; set; }

    public string? SkillName { get; set; }

    public bool SkillActivated { get; set; }

    /// <summary>Whether a requisition draft was staged during the turn.</summary>
    public bool RequisitionDraftStaged { get; set; }

    /// <summary>Whether a requisition draft is currently presented to the user.</summary>
    public bool RequisitionDraftPresented { get; set; }

    /// <summary>Whether a requisition draft was confirmed by the user.</summary>
    public bool RequisitionDraftConfirmed { get; set; }

    /// <summary>
    /// Whether a <c>create_requisition</c> call in this turn actually persisted a
    /// requisition. False when the call was refused — most importantly when it
    /// was refused for want of a confirmed draft.
    /// </summary>
    public bool RequisitionPersisted { get; set; }

    public List<RagRetrievedItem> RetrievedItems { get; set; } = new();

    public List<RagToolCall> ToolCalls { get; set; } = new();

    public string Answer { get; set; } = string.Empty;

    public int RetrievedCount { get; set; }

    public bool UsedNoContextFallback { get; set; }
}

public sealed class RagRetrievedItem
{
    public string PurchaseRequisitionId { get; set; } = string.Empty;

    public string SupplierCode { get; set; } = string.Empty;

    public string SupplierName { get; set; } = string.Empty;

    public string Item { get; set; } = string.Empty;

    public string ItemName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public double? Similarity { get; set; }

    public static RagRetrievedItem From(PurchaseRequisition r, double? similarity) => new()
    {
        PurchaseRequisitionId = r.PurchaseRequisitionId,
        SupplierCode = r.SupplierCode,
        SupplierName = r.SupplierName,
        Item = r.Item,
        ItemName = r.ItemName,
        Description = r.Description,
        Similarity = similarity,
    };
}

public sealed class RagToolCall
{
    public string Name { get; set; } = string.Empty;

    public IReadOnlyDictionary<string, object?> Arguments { get; set; } =
        new Dictionary<string, object?>();
}
