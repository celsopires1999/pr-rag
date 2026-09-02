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

    public List<RagRetrievedItem> RetrievedItems { get; set; } = new();

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
