using PrRag.Application.Abstractions;
using PrRag.Application.DTOs;

namespace PrRag.Application.Services;

public sealed class StatusService : IStatusService
{
    private readonly IPurchaseRequisitionRepository _repository;

    public StatusService(IPurchaseRequisitionRepository repository)
    {
        _repository = repository;
    }

    public async Task<SystemStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return new SystemStatus
        {
            RequisitionCount = await _repository.CountAsync(cancellationToken),
            EmbeddedCount = await _repository.CountEmbeddedAsync(cancellationToken),
            LastSync = await _repository.GetLastSyncAsync(cancellationToken),
        };
    }
}
