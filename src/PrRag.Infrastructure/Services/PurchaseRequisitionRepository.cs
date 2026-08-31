using Microsoft.EntityFrameworkCore;
using PrRag.Application.Abstractions;
using PrRag.Application.Domain;
using PrRag.Application.DTOs;
using PrRag.Infrastructure.Persistence;

namespace PrRag.Infrastructure.Services;

public sealed class PurchaseRequisitionRepository : IPurchaseRequisitionRepository
{
    private readonly PrRagDbContext _db;

    public PurchaseRequisitionRepository(PrRagDbContext db)
    {
        _db = db;
    }

    public async Task<Dictionary<string, PurchaseRequisition>> GetByKeysAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default)
    {
        var keySet = keys.ToHashSet();
        return await _db.PurchaseRequisitions
            .AsNoTracking()
            .Where(x => keySet.Contains(x.PurchaseRequisitionId))
            .ToDictionaryAsync(x => x.PurchaseRequisitionId, cancellationToken);
    }

    public async Task UpsertAllAsync(
        IReadOnlyCollection<PurchaseRequisition> requisitions,
        CancellationToken cancellationToken = default)
    {
        foreach (var requisition in requisitions)
        {
            var existing = await _db.PurchaseRequisitions
                .FirstOrDefaultAsync(x => x.PurchaseRequisitionId == requisition.PurchaseRequisitionId, cancellationToken);

            if (existing is null)
            {
                _db.PurchaseRequisitions.Add(requisition);
            }
            else
            {
                existing.SupplierCode = requisition.SupplierCode;
                existing.SupplierName = requisition.SupplierName;
                existing.Item = requisition.Item;
                existing.ItemName = requisition.ItemName;
                existing.Description = requisition.Description;
                existing.Embedding = requisition.Embedding;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RequisitionSearchResult>> SearchAsync(
        float[] queryEmbedding,
        int topK,
        double minSimilarity,
        CancellationToken cancellationToken = default)
    {
        var queryVector = new Pgvector.Vector(queryEmbedding);

        var sql = """
            SELECT
                purchase_requisition AS "PurchaseRequisitionId",
                supplier_code AS "SupplierCode",
                supplier_name AS "SupplierName",
                item AS "Item",
                item_name AS "ItemName",
                description AS "Description",
                1 - (embedding <=> {0}) AS "Similarity"
            FROM purchase_requisitions
            WHERE embedding IS NOT NULL
              AND 1 - (embedding <=> {0}) >= {1}
            ORDER BY embedding <=> {0}
            LIMIT {2}
            """;

        var rows = await _db.Database.SqlQueryRaw<SearchRow>(sql, queryVector, minSimilarity, topK)
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new RequisitionSearchResult(r.ToEntity(), r.Similarity))
            .ToList();
    }

    private sealed class SearchRow
    {
        public string PurchaseRequisitionId { get; set; } = string.Empty;
        public string SupplierCode { get; set; } = string.Empty;
        public string SupplierName { get; set; } = string.Empty;
        public string Item { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public double Similarity { get; set; }

        public PurchaseRequisition ToEntity() => new()
        {
            PurchaseRequisitionId = PurchaseRequisitionId,
            SupplierCode = SupplierCode,
            SupplierName = SupplierName,
            Item = Item,
            ItemName = ItemName,
            Description = Description,
        };
    }

    public async Task<IReadOnlyList<PurchaseRequisition>> SearchByCodesAsync(
        IReadOnlyCollection<string>? items,
        IReadOnlyCollection<string>? supplierCodes,
        int topK,
        CancellationToken cancellationToken = default)
    {
        if ((items is null || items.Count == 0) && (supplierCodes is null || supplierCodes.Count == 0))
        {
            return Array.Empty<PurchaseRequisition>();
        }

        IQueryable<PurchaseRequisition> query = _db.PurchaseRequisitions;

        if (items is { Count: > 0 })
        {
            var itemSet = items.ToHashSet();
            query = query.Where(x => itemSet.Contains(x.Item));
        }

        if (supplierCodes is { Count: > 0 })
        {
            var codeSet = supplierCodes.ToHashSet();
            query = query.Where(x => codeSet.Contains(x.SupplierCode));
        }

        return await query
            .Take(topK)
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountAsync(CancellationToken cancellationToken = default)
        => _db.PurchaseRequisitions.CountAsync(cancellationToken);

    public Task<int> CountEmbeddedAsync(CancellationToken cancellationToken = default)
        => _db.PurchaseRequisitions.CountAsync(x => x.Embedding != null, cancellationToken);

    public async Task<DateTime?> GetLastSyncAsync(CancellationToken cancellationToken = default)
        => (await _db.DataStatuses.FirstOrDefaultAsync(cancellationToken))?.LastSync;

    public async Task SetLastSyncAsync(DateTime timestamp, CancellationToken cancellationToken = default)
    {
        var status = await _db.DataStatuses.FirstOrDefaultAsync(cancellationToken);
        if (status is null)
        {
            _db.DataStatuses.Add(new DataStatus { Id = 1, LastSync = timestamp });
        }
        else
        {
            status.LastSync = timestamp;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
