using PrRag.Application.DTOs;

namespace PrRag.Application.Abstractions;

public interface IIngestionService
{
    Task<IngestResult> IngestAsync(CancellationToken cancellationToken = default);
}
