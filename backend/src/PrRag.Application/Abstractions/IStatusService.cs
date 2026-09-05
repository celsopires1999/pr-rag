using PrRag.Application.DTOs;

namespace PrRag.Application.Abstractions;

public interface IStatusService
{
    Task<SystemStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}
