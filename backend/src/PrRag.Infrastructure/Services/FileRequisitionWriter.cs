using System.Text.Json;
using Microsoft.Extensions.Options;
using PrRag.Application.Abstractions;
using PrRag.Application.Configuration;
using PrRag.Application.DTOs;

namespace PrRag.Infrastructure.Services;

/// <summary>
/// Persists new purchase requisitions as JSON files under the configured
/// directory, writing a unique file per requisition with an atomic temp+rename.
/// Missing or invalid required fields produce an error and write no file.
/// </summary>
public sealed class FileRequisitionWriter : IRequisitionWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _directory;
    private readonly object _initLock = new();
    private bool _initialized;

    public FileRequisitionWriter(IOptions<RequisitionsSettings> settings)
    {
        _directory = settings.Value.Directory;
    }

    public async Task<RequisitionWriteResult> WriteAsync(
        NewPurchaseRequisition requisition,
        CancellationToken cancellationToken = default)
    {
        var validationError = Validate(requisition);
        if (validationError is not null)
        {
            return RequisitionWriteResult.Fail(validationError);
        }

        EnsureInitialized();

        var fileName = $"requisition-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json";
        var path = Path.Combine(_directory, fileName);
        var tempPath = path + ".tmp";

        await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(requisition, JsonOptions), cancellationToken);
        File.Move(tempPath, path, overwrite: true);

        return RequisitionWriteResult.Ok(fileName);
    }

    private static string? Validate(NewPurchaseRequisition requisition)
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(requisition.SupplierCode))
        {
            problems.Add("SupplierCode");
        }

        if (string.IsNullOrWhiteSpace(requisition.Item))
        {
            problems.Add("Item");
        }

        if (string.IsNullOrWhiteSpace(requisition.Description))
        {
            problems.Add("Description");
        }

        if (requisition.Quantity <= 0)
        {
            problems.Add("Quantity (must be a positive number)");
        }

        if (string.IsNullOrWhiteSpace(requisition.Date) || !DateOnly.TryParse(requisition.Date, out _))
        {
            problems.Add("Date (must be ISO yyyy-MM-dd)");
        }

        if (string.IsNullOrWhiteSpace(requisition.Requester))
        {
            problems.Add("Requester");
        }

        return problems.Count == 0
            ? null
            : $"Missing or invalid required fields: {string.Join(", ", problems)}.";
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

            Directory.CreateDirectory(_directory);
            _initialized = true;
        }
    }
}