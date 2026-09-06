using PrRag.Application.DTOs;

namespace PrRag.Application.Abstractions;

public sealed record RequisitionWriteResult(bool Success, string? RequisitionId, string? Error)
{
    public static RequisitionWriteResult Ok(string requisitionId) => new(true, requisitionId, null);

    public static RequisitionWriteResult Fail(string error) => new(false, null, error);
}

public interface IRequisitionWriter
{
    /// <summary>
    /// Persists a new purchase requisition in the database.
    /// Returns the created requisition id on success, or an error (persisting
    /// nothing) for missing or invalid required fields.
    /// </summary>
    Task<RequisitionWriteResult> WriteAsync(
        NewPurchaseRequisition requisition,
        CancellationToken cancellationToken = default);
}