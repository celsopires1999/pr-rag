namespace PrRag.Application.DTOs;

/// <summary>Read-only projection of a created requisition for the listing endpoints.</summary>
public sealed class CreatedRequisitionDto
{
    public Guid Id { get; set; }

    public string SupplierCode { get; set; } = string.Empty;

    public string Item { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public string Date { get; set; } = string.Empty;

    public string Requester { get; set; } = string.Empty;

    public string? SessionId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A single page of created requisitions plus the total matching count.</summary>
public sealed class CreatedRequisitionPage
{
    public IReadOnlyList<CreatedRequisitionDto> Items { get; set; } = Array.Empty<CreatedRequisitionDto>();

    public int Total { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }
}

/// <summary>Determines the ordering of a created-requisition query.</summary>
public enum CreatedRequisitionSortField
{
    SupplierCode,
    Item,
    Description,
    Quantity,
    Date,
    Requester,
    CreatedAt,
}

/// <summary>Inputs for a created-requisition listing query: pagination, sort, and filters.</summary>
public sealed class CreatedRequisitionQueryInput
{
    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;

    public CreatedRequisitionSortField SortField { get; set; } = CreatedRequisitionSortField.CreatedAt;

    public bool SortDescending { get; set; } = true;

    public string? SupplierCode { get; set; }

    public string? Item { get; set; }

    public string? Description { get; set; }

    public string? Requester { get; set; }

    public decimal? MinQuantity { get; set; }

    public decimal? MaxQuantity { get; set; }

    public DateOnly? DateFrom { get; set; }

    public DateOnly? DateTo { get; set; }

    public DateOnly? CreatedFrom { get; set; }

    public DateOnly? CreatedTo { get; set; }

    public string? SessionId { get; set; }
}