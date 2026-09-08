namespace PrRag.Application.Domain;

public sealed class CreatedRequisition
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string SupplierCode { get; set; } = string.Empty;

    public string Item { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public string Date { get; set; } = string.Empty;

    public string Requester { get; set; } = string.Empty;

    public string? SessionId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}