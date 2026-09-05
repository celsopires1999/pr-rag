using System.Text.Json;
using PrRag.Application.DTOs;

namespace PrRag.Application.Services;

public sealed class PurchaseRequisitionFileLoader
{
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<IReadOnlyList<PurchaseRequisitionImport>> LoadAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(filePath);
        var records = await JsonSerializer.DeserializeAsync<List<PurchaseRequisitionImport>>(
            stream, _options, cancellationToken);

        if (records is null)
        {
            return Array.Empty<PurchaseRequisitionImport>();
        }

        records.RemoveAll(r => string.IsNullOrWhiteSpace(r.PurchaseRequisition));
        return records;
    }
}
