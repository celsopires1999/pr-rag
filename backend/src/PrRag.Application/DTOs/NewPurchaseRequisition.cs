namespace PrRag.Application.DTOs;

public sealed class NewPurchaseRequisition
{
    public string SupplierCode { get; set; } = string.Empty;

    public string Item { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public string Date { get; set; } = string.Empty;

    public string Requester { get; set; } = string.Empty;

    public string? SessionId { get; set; }

    /// <summary>
    /// Builds the persistence payload from a staged draft, so the values written
    /// are the confirmed ones rather than whatever the model re-sent.
    /// </summary>
    public static NewPurchaseRequisition From(Domain.RequisitionDraft draft) => new()
    {
        SupplierCode = draft.SupplierCode,
        Item = draft.Item,
        Description = draft.Description,
        Quantity = draft.Quantity,
        Date = draft.Date,
        Requester = draft.Requester,
    };

    public string? Validate()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(SupplierCode))
        {
            problems.Add("SupplierCode");
        }

        if (string.IsNullOrWhiteSpace(Item))
        {
            problems.Add("Item");
        }

        if (string.IsNullOrWhiteSpace(Description))
        {
            problems.Add("Description");
        }

        if (Quantity <= 0)
        {
            problems.Add("Quantity (must be a positive number)");
        }

        if (string.IsNullOrWhiteSpace(Date) || !DateOnly.TryParse(Date, out _))
        {
            problems.Add("Date (must be ISO yyyy-MM-dd)");
        }

        if (string.IsNullOrWhiteSpace(Requester))
        {
            problems.Add("Requester");
        }

        return problems.Count == 0
            ? null
            : $"Missing or invalid required fields: {string.Join(", ", problems)}.";
    }
}