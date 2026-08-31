using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PrRag.Application.Abstractions;
using PrRag.Application.DTOs;
using PrRag.Infrastructure.Persistence;
using Xunit;

namespace PrRag.Tests;

public class IngestionDiffTests : IAsyncLifetime
{
    private static string ConnectionTemplate =>
        Environment.GetEnvironmentVariable("TEST_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Username=prrag;Password=prrag";

    private readonly string _dbName = $"prrag_test_{Guid.NewGuid():N}";
    private string _connectionString = null!;
    private ServiceProvider? _provider;
    private FakeEmbeddingService? _embeddings;
    private string _dataDir = null!;

    public async Task InitializeAsync()
    {
        _connectionString = $"{ConnectionTemplate};Database={_dbName}";

        (_provider, _embeddings, _dataDir) = IntegrationServiceFactory.Create(_connectionString);

        await TestDatabase.MigrateAndReloadTypesAsync(_provider);
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }

        if (!string.IsNullOrEmpty(_dataDir) && Directory.Exists(_dataDir))
        {
            Directory.Delete(_dataDir, recursive: true);
        }
    }

    [Fact]
    public async Task Initial_import_embeds_all_rows()
    {
        var records = new[]
        {
            Sample("PR00000001", "Acme Industrial Supply", "Hydraulic Pump"),
            Sample("PR00000002", "Beta Components Ltd", "Ball Bearings"),
            Sample("PR00000003", "Gamma Metals Corp", "Steel Sheet"),
        };
        await WriteJsonAsync(records);

        var result = await RunIngestAsync();

        Assert.Equal(3, result.TotalRecords);
        Assert.Equal(3, result.Inserted);
        Assert.Equal(0, result.Updated);
        Assert.Equal(3, result.Embedded);
        Assert.Equal(3, _embeddings!.CallCount);
        Assert.Equal(3, await CountRowsAsync());
        Assert.Equal(3, await CountEmbeddedAsync());
    }

    [Fact]
    public async Task Reimport_with_no_changes_does_not_reembed()
    {
        var records = new[]
        {
            Sample("PR00000001", "Acme Industrial Supply", "Hydraulic Pump"),
            Sample("PR00000002", "Beta Components Ltd", "Ball Bearings"),
        };
        await WriteJsonAsync(records);

        var first = await RunIngestAsync();
        var callsAfterFirst = _embeddings!.CallCount;

        var second = await RunIngestAsync();

        Assert.Equal(2, first.Embedded);
        Assert.Equal(0, second.Updated);
        Assert.Equal(0, second.Embedded);
        Assert.Equal(callsAfterFirst, _embeddings.CallCount);
    }

    [Fact]
    public async Task Changed_and_new_rows_are_upserted_and_reembedded()
    {
        var records = new[]
        {
            Sample("PR00000001", "Acme Industrial Supply", "Hydraulic Pump"),
            Sample("PR00000002", "Beta Components Ltd", "Ball Bearings"),
        };
        await WriteJsonAsync(records);
        await RunIngestAsync();

        var updated = records[0];
        updated.Description = "Completely changed description text for the pump.";
        var newRecord = Sample("PR00000004", "Delta Tools & Machinery", "Cutting Tools");
        await WriteJsonAsync(new[] { updated, records[1], newRecord });

        var result = await RunIngestAsync();

        Assert.Equal(1, result.Updated);
        Assert.Equal(1, result.Inserted);
        Assert.Equal(2, result.Embedded); // one changed (PR1) + one new (PR4)
        Assert.Equal(3, await CountRowsAsync());

        var pr1 = await GetAsync("PR00000001");
        Assert.Equal("Completely changed description text for the pump.", pr1.Description);
    }

    private async Task<IngestResult> RunIngestAsync()
    {
        using var scope = _provider!.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IIngestionService>();
        return await service.IngestAsync();
    }

    private async Task<int> CountRowsAsync()
    {
        using var scope = _provider!.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPurchaseRequisitionRepository>();
        return await repo.CountAsync();
    }

    private async Task<int> CountEmbeddedAsync()
    {
        using var scope = _provider!.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPurchaseRequisitionRepository>();
        return await repo.CountEmbeddedAsync();
    }

    private async Task<PrRag.Application.Domain.PurchaseRequisition> GetAsync(string id)
    {
        using var scope = _provider!.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPurchaseRequisitionRepository>();
        var rows = await repo.GetByKeysAsync(new[] { id });
        return rows[id];
    }

    private async Task WriteJsonAsync(IEnumerable<PurchaseRequisitionImport> records)
    {
        var path = Path.Combine(_dataDir, "purchase.json");
        var json = JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
    }

    private static PurchaseRequisitionImport Sample(string pr, string supplier, string itemName)
    {
        var supplierCode = supplier.StartsWith("Acme") ? "SUP000001"
            : supplier.StartsWith("Beta") ? "SUP000002"
            : supplier.StartsWith("Gamma") ? "SUP000003"
            : "SUP000004";

        return new PurchaseRequisitionImport
        {
            PurchaseRequisition = pr,
            SupplierCode = supplierCode,
            SupplierName = supplier,
            Item = "ITM0001",
            ItemName = itemName,
            Description = $"Procurement of {itemName} for maintenance operations.",
        };
    }
}
