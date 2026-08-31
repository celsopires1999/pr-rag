using System.Text.Json;
using Microsoft.Extensions.Options;
using PrRag.Application.Abstractions;
using PrRag.Application.Configuration;
using PrRag.Application.DTOs;

namespace PrRag.Infrastructure.Services;

public sealed class FileRagReportWriter : IRagReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _outputDirectory;
    private readonly object _initLock = new();
    private bool _initialized;

    public FileRagReportWriter(IOptions<ReportSettings> reportSettings)
    {
        _outputDirectory = reportSettings.Value.OutputDirectory;
    }

    public async Task WriteAsync(RagQueryReport report, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        var fileName = $"{report.Timestamp:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json";
        var path = Path.Combine(_outputDirectory, fileName);

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, report, JsonOptions, cancellationToken);
    }

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        lock (_initLock)
        {
            if (_initialized)
            {
                return;
            }

            Directory.CreateDirectory(_outputDirectory);
            _initialized = true;
        }
    }
}
