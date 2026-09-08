using PrRag.Application.DTOs;

namespace PrRag.Application.Abstractions;

/// <summary>
/// Reads created requisitions for the listing endpoint: server-side pagination,
/// whitelisted column ordering, per-column filters, and optional session scoping.
/// Read-only by design — creation stays agent-mediated.
/// </summary>
public interface ICreatedRequisitionQuery
{
    Task<CreatedRequisitionPage> QueryAsync(
        CreatedRequisitionQueryInput input,
        CancellationToken cancellationToken = default);
}