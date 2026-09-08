using Microsoft.EntityFrameworkCore;
using PrRag.Application.Abstractions;
using PrRag.Application.DTOs;
using PrRag.Application.Domain;
using PrRag.Infrastructure.Persistence;

namespace PrRag.Infrastructure.Services;

/// <summary>
/// Reads created requisitions from the dedicated "created" schema with
/// server-side pagination, whitelisted column ordering, per-column filters
/// (case-insensitive contains for text, inclusive ranges otherwise), and
/// optional session scoping.
/// </summary>
public sealed class CreatedRequisitionQuery : ICreatedRequisitionQuery
{
    private readonly PrRagDbContext _db;

    public CreatedRequisitionQuery(PrRagDbContext db)
    {
        _db = db;
    }

    public async Task<CreatedRequisitionPage> QueryAsync(
        CreatedRequisitionQueryInput input,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, input.Page);
        var pageSize = Math.Clamp(input.PageSize, 1, 100);

        var query = ApplyFilters(_db.CreatedRequisitions.AsNoTracking(), input);
        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(input.SortField, input.SortDescending)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new CreatedRequisitionDto
            {
                Id = r.Id,
                SupplierCode = r.SupplierCode,
                Item = r.Item,
                Description = r.Description,
                Quantity = r.Quantity,
                Date = r.Date,
                Requester = r.Requester,
                SessionId = r.SessionId,
                CreatedAt = r.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        return new CreatedRequisitionPage
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    private static IQueryable<CreatedRequisition> ApplyFilters(
        IQueryable<CreatedRequisition> query,
        CreatedRequisitionQueryInput input)
    {
        if (!string.IsNullOrWhiteSpace(input.SupplierCode))
        {
            query = query.Where(r => EF.Functions.ILike(r.SupplierCode, LikePattern(input.SupplierCode)));
        }

        if (!string.IsNullOrWhiteSpace(input.Item))
        {
            query = query.Where(r => EF.Functions.ILike(r.Item, LikePattern(input.Item)));
        }

        if (!string.IsNullOrWhiteSpace(input.Description))
        {
            query = query.Where(r => EF.Functions.ILike(r.Description, LikePattern(input.Description)));
        }

        if (!string.IsNullOrWhiteSpace(input.Requester))
        {
            query = query.Where(r => EF.Functions.ILike(r.Requester, LikePattern(input.Requester)));
        }

        if (input.MinQuantity is not null)
        {
            query = query.Where(r => r.Quantity >= input.MinQuantity.Value);
        }

        if (input.MaxQuantity is not null)
        {
            query = query.Where(r => r.Quantity <= input.MaxQuantity.Value);
        }

        // `Date` is persisted as an ISO yyyy-MM-dd string, so lexical comparison == chronological.
        if (input.DateFrom is not null)
        {
            var from = input.DateFrom.Value.ToString("yyyy-MM-dd");
            query = query.Where(r => string.Compare(r.Date, from) >= 0);
        }

        if (input.DateTo is not null)
        {
            var to = input.DateTo.Value.ToString("yyyy-MM-dd");
            query = query.Where(r => string.Compare(r.Date, to) <= 0);
        }

        if (input.CreatedFrom is not null)
        {
            var from = new DateTimeOffset(input.CreatedFrom.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(r => r.CreatedAt >= from);
        }

        if (input.CreatedTo is not null)
        {
            // Inclusive: the day boundary is exclusive of the following midnight.
            var to = new DateTimeOffset(input.CreatedTo.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(r => r.CreatedAt < to);
        }

        if (!string.IsNullOrWhiteSpace(input.SessionId))
        {
            var sessionId = input.SessionId.Trim();
            query = query.Where(r => r.SessionId == sessionId);
        }

        return query;
    }

    private static string LikePattern(string value)
        => $"%{value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
}

internal static class CreatedRequisitionQueryExtensions
{
    public static IOrderedQueryable<CreatedRequisition> OrderBy(
        this IQueryable<CreatedRequisition> query,
        CreatedRequisitionSortField field,
        bool descending)
    {
        return field switch
        {
            CreatedRequisitionSortField.SupplierCode => descending
                ? query.OrderByDescending(r => r.SupplierCode)
                : query.OrderBy(r => r.SupplierCode),
            CreatedRequisitionSortField.Item => descending
                ? query.OrderByDescending(r => r.Item)
                : query.OrderBy(r => r.Item),
            CreatedRequisitionSortField.Description => descending
                ? query.OrderByDescending(r => r.Description)
                : query.OrderBy(r => r.Description),
            CreatedRequisitionSortField.Quantity => descending
                ? query.OrderByDescending(r => r.Quantity)
                : query.OrderBy(r => r.Quantity),
            CreatedRequisitionSortField.Date => descending
                ? query.OrderByDescending(r => r.Date)
                : query.OrderBy(r => r.Date),
            CreatedRequisitionSortField.Requester => descending
                ? query.OrderByDescending(r => r.Requester)
                : query.OrderBy(r => r.Requester),
            _ => descending
                ? query.OrderByDescending(r => r.CreatedAt)
                : query.OrderBy(r => r.CreatedAt),
        };
    }
}