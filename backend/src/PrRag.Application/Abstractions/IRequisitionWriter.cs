using PrRag.Application.DTOs;

namespace PrRag.Application.Abstractions;

public sealed record RequisitionWriteResult(bool Success, string? FileName, string? Error)
{
    public static RequisitionWriteResult Ok(string fileName) => new(true, fileName, null);

    public static RequisitionWriteResult Fail(string error) => new(false, null, error);
}

public interface IRequisitionWriter
{
    /// <summary>
    /// Persists a new purchase requisition as a JSON file in the configured directory.
    /// Returns the created file name on success, or an error (writing no file) for
    /// missing or invalid required fields.
    /// </summary>
    Task<RequisitionWriteResult> WriteAsync(
        NewPurchaseRequisition requisition,
        CancellationToken cancellationToken = default);
}