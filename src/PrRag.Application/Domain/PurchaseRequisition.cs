namespace PrRag.Application.Domain;

public sealed class PurchaseRequisition
{
    public const int EmbeddingDimensions = 1536;

    public string PurchaseRequisitionId { get; set; } = string.Empty;

    public string SupplierCode { get; set; } = string.Empty;

    public string SupplierName { get; set; } = string.Empty;

    public string Item { get; set; } = string.Empty;

    public string ItemName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public float[]? Embedding { get; set; }

    public string EmbeddingSource =>
        $"Supplier Code: {SupplierCode} / Supplier Name: {SupplierName} / Item: {Item} / Item Name: {ItemName} / Description: {Description}";

    public bool ContentEquals(PurchaseRequisition other)
    {
        return SupplierCode == other.SupplierCode
            && SupplierName == other.SupplierName
            && Item == other.Item
            && ItemName == other.ItemName
            && Description == other.Description;
    }
}
