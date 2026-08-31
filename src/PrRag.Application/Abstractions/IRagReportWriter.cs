using PrRag.Application.DTOs;

namespace PrRag.Application.Abstractions;

public interface IRagReportWriter
{
    Task WriteAsync(RagQueryReport report, CancellationToken cancellationToken = default);
}
