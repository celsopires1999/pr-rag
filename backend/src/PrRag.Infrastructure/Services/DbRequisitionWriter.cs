using Microsoft.EntityFrameworkCore;
using PrRag.Application.Abstractions;
using PrRag.Application.Domain;
using PrRag.Application.DTOs;
using PrRag.Infrastructure.Persistence;

namespace PrRag.Infrastructure.Services;

/// <summary>
/// Persists new purchase requisitions in the dedicated "created" schema,
/// one row per requisition with a client-generated Guid id.
/// Missing or invalid required fields produce an error and persist nothing.
/// </summary>
public sealed class DbRequisitionWriter : IRequisitionWriter
{
    private readonly PrRagDbContext _dbContext;

    public DbRequisitionWriter(PrRagDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<RequisitionWriteResult> WriteAsync(
        NewPurchaseRequisition requisition,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var validationError = requisition.Validate();
        if (validationError is not null)
        {
            return RequisitionWriteResult.Fail(validationError);
        }

        var created = new CreatedRequisition
        {
            Id = Guid.NewGuid(),
            SupplierCode = requisition.SupplierCode,
            Item = requisition.Item,
            Description = requisition.Description,
            Quantity = requisition.Quantity,
            Date = requisition.Date,
            Requester = requisition.Requester,
            SessionId = sessionId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _dbContext.CreatedRequisitions.Add(created);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return RequisitionWriteResult.Ok(created.Id.ToString("N"));
    }
}