namespace PrRag.Application.DTOs;

public sealed class PurchaseRequisitionImport
{
    public string PurchaseRequisition { get; set; } = string.Empty;
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string Item { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
