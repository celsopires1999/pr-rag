using Microsoft.Extensions.Options;
using PrRag.Application.Abstractions;
using PrRag.Application.Configuration;
using PrRag.Application.Domain;
using PrRag.Application.DTOs;

namespace PrRag.Application.Services;

public sealed class IngestionService : IIngestionService
{
    private readonly IPurchaseRequisitionRepository _repository;
    private readonly IEmbeddingService _embeddingService;
    private readonly DataSettings _dataSettings;
    private readonly PurchaseRequisitionFileLoader _fileLoader;

    public IngestionService(
        IPurchaseRequisitionRepository repository,
        IEmbeddingService embeddingService,
        IOptions<DataSettings> dataSettings,
        PurchaseRequisitionFileLoader fileLoader)
    {
        _repository = repository;
        _embeddingService = embeddingService;
        _dataSettings = dataSettings.Value;
        _fileLoader = fileLoader;
    }

    public async Task<IngestResult> IngestAsync(CancellationToken cancellationToken = default)
    {
        var records = await _fileLoader.LoadAsync(_dataSettings.FilePath, cancellationToken);
        if (records.Count == 0)
        {
            return new IngestResult();
        }

        var keys = records.Select(r => r.PurchaseRequisition).ToList();
        var existing = await _repository.GetByKeysAsync(keys, cancellationToken);

        var inserted = 0;
        var updated = 0;
        var changedRows = new List<PurchaseRequisition>();

        foreach (var record in records)
        {
            var entity = Map(record);

            if (existing.TryGetValue(record.PurchaseRequisition, out var current))
            {
                if (!current.ContentEquals(entity))
                {
                    updated++;
                    changedRows.Add(entity);
                }
            }
            else
            {
                inserted++;
                changedRows.Add(entity);
            }
        }

        var sources = changedRows.Select(r => r.EmbeddingSource).ToList();
        var embeddings = await _embeddingService.GenerateBatchAsync(sources, cancellationToken);
        for (var i = 0; i < changedRows.Count; i++)
        {
            changedRows[i].Embedding = embeddings[i];
        }

        await _repository.UpsertAllAsync(changedRows, cancellationToken);
        await _repository.SetLastSyncAsync(DateTime.UtcNow, cancellationToken);

        return new IngestResult
        {
            TotalRecords = records.Count,
            Inserted = inserted,
            Updated = updated,
            Embedded = changedRows.Count,
        };
    }

    private static PurchaseRequisition Map(PurchaseRequisitionImport record) => new()
    {
        PurchaseRequisitionId = record.PurchaseRequisition,
        SupplierCode = record.SupplierCode,
        SupplierName = record.SupplierName,
        Item = record.Item,
        ItemName = record.ItemName,
        Description = record.Description,
    };
}
