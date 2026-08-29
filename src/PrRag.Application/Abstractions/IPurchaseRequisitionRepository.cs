using PrRag.Application.Domain;
using PrRag.Application.DTOs;

namespace PrRag.Application.Abstractions;

public interface IPurchaseRequisitionRepository
{
    Task<Dictionary<string, PurchaseRequisition>> GetByKeysAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default);

    Task UpsertAllAsync(
        IReadOnlyCollection<PurchaseRequisition> requisitions,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PurchaseRequisition>> SearchAsync(
        float[] queryEmbedding,
        int topK,
        double minSimilarity,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PurchaseRequisition>> SearchByCodesAsync(
        IReadOnlyCollection<string>? items,
        IReadOnlyCollection<string>? supplierCodes,
        int topK,
        CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);

    Task<int> CountEmbeddedAsync(CancellationToken cancellationToken = default);

    Task<DateTime?> GetLastSyncAsync(CancellationToken cancellationToken = default);

    Task SetLastSyncAsync(DateTime timestamp, CancellationToken cancellationToken = default);
}
