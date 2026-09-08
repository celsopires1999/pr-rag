using Microsoft.AspNetCore.Http;
using PrRag.Application.Abstractions;
using PrRag.Application.DTOs;

namespace PrRag.Api;

/// <summary>
/// Read-only listing of created purchase requisitions. Pagination, sorting,
/// per-column filters, and optional session scoping are resolved server-side.
/// </summary>
public static class CreatedRequisitionEndpoints
{
    private static readonly Dictionary<string, CreatedRequisitionSortField> SortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["supplierCode"] = CreatedRequisitionSortField.SupplierCode,
        ["item"] = CreatedRequisitionSortField.Item,
        ["description"] = CreatedRequisitionSortField.Description,
        ["quantity"] = CreatedRequisitionSortField.Quantity,
        ["date"] = CreatedRequisitionSortField.Date,
        ["requester"] = CreatedRequisitionSortField.Requester,
        ["createdAt"] = CreatedRequisitionSortField.CreatedAt,
    };

    public static async Task<IResult> List(
        ICreatedRequisitionQuery query,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var queryString = request.Query;

        var page = ParseInt(queryString, "page", 1, min: 1);
        var pageSize = ParseInt(queryString, "pageSize", 20, min: 1, max: 100);

        var sortField = CreatedRequisitionSortField.CreatedAt;
        if (SortFields.TryGetValue(queryString["sortBy"].ToString(), out var requestedSort))
        {
            sortField = requestedSort;
        }

        var sortDescending = !string.Equals(
            queryString["sortDir"].ToString(),
            "asc",
            StringComparison.OrdinalIgnoreCase);

        var input = new CreatedRequisitionQueryInput
        {
            Page = page,
            PageSize = pageSize,
            SortField = sortField,
            SortDescending = sortDescending,
            SupplierCode = Trimmed(queryString["supplierCode"]),
            Item = Trimmed(queryString["item"]),
            Description = Trimmed(queryString["description"]),
            Requester = Trimmed(queryString["requester"]),
            MinQuantity = ParseNullableDecimal(queryString, "minQuantity"),
            MaxQuantity = ParseNullableDecimal(queryString, "maxQuantity"),
            DateFrom = ParseNullableDate(queryString, "dateFrom"),
            DateTo = ParseNullableDate(queryString, "dateTo"),
            CreatedFrom = ParseNullableDate(queryString, "createdFrom"),
            CreatedTo = ParseNullableDate(queryString, "createdTo"),
            SessionId = Trimmed(queryString["sessionId"]),
        };

        var result = await query.QueryAsync(input, cancellationToken);
        return Results.Ok(result);
    }

    private static string? Trimmed(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int ParseInt(IQueryCollection query, string key, int fallback, int min, int max = int.MaxValue)
        => int.TryParse(query[key], out var value) ? Math.Clamp(value, min, max) : fallback;

    private static decimal? ParseNullableDecimal(IQueryCollection query, string key)
        => decimal.TryParse(query[key], out var value) ? value : null;

    private static DateOnly? ParseNullableDate(IQueryCollection query, string key)
        => DateOnly.TryParse(query[key], out var value) ? value : null;
}