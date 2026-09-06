namespace PrRag.Application.DTOs;

public sealed class NewPurchaseRequisition
{
    public string SupplierCode { get; set; } = string.Empty;

    public string Item { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public string Date { get; set; } = string.Empty;

    public string Requester { get; set; } = string.Empty;
}