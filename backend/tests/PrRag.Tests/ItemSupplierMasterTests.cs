using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PrRag.Application.Abstractions;
using PrRag.Application.Domain;
using PrRag.Application.DTOs;
using Xunit;

namespace PrRag.Tests;

public class ItemSupplierMasterTests : IAsyncLifetime
{
    private static string ConnectionTemplate => TestDatabase.ConnectionStringTemplate;

    private readonly string _dbName = $"prrag_test_{Guid.NewGuid():N}";
    private string _connectionString = null!;
    private ServiceProvider? _provider;
    private string _dataDir = null!;

    public async Task InitializeAsync()
    {
        _connectionString = $"{ConnectionTemplate};Database={_dbName}";
        (_provider, _, _dataDir) = IntegrationServiceFactory.Create(_connectionString);
        await TestDatabase.MigrateAndReloadTypesAsync(_provider);

        var records = new[]
        {
            Requisition("PR00000001", "SUP000001", "ITM0001", "Acme Industrial Supply", "Hydraulic Pump"),
            Requisition("PR00000002", "SUP000001", "ITM0002", "Acme Industrial Supply", "Ball Bearings"),
            Requisition("PR00000003", "SUP000002", "ITM0003", "Beta Components Ltd", "Steel Sheet"),
            Requisition("PR00000004", "SUP000001", "ITM0001", "Acme Industrial Supply", "Hydraulic Pump"),
        };
        await WriteJsonAsync(records);

        using var scope = _provider.CreateScope();
        var ingestion = scope.ServiceProvider.GetRequiredService<IIngestionService>();
        await ingestion.IngestAsync();
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
            await TestDatabase.DropDatabaseAsync(_dbName);
        }

        if (!string.IsNullOrEmpty(_dataDir) && Directory.Exists(_dataDir))
        {
            Directory.Delete(_dataDir, recursive: true);
        }
    }

    [Fact]
    public async Task SearchBySupplierCode_returns_distinct_items_for_supplier()
    {
        using var scope = _provider!.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPurchaseRequisitionRepository>();

        var results = await repo.SearchItemSupplierMasterAsync(supplierCode: "SUP000001", item: null);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal("SUP000001", r.SupplierCode));
        Assert.Contains(results, r => r.Item == "ITM0001" && r.ItemName == "Hydraulic Pump");
        Assert.Contains(results, r => r.Item == "ITM0002" && r.ItemName == "Ball Bearings");
    }

    [Fact]
    public async Task SearchByItemCode_returns_distinct_suppliers_for_item()
    {
        using var scope = _provider!.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPurchaseRequisitionRepository>();

        var results = await repo.SearchItemSupplierMasterAsync(item: "ITM0001", supplierCode: null);

        // ITM0001 appears in rows 1 and 4 (both SUP000001) — should deduplicate
        var distinct = Assert.Single(results);
        Assert.Equal("SUP000001", distinct.SupplierCode);
        Assert.Equal("Acme Industrial Supply", distinct.SupplierName);
        Assert.Equal("ITM0001", distinct.Item);
        Assert.Equal("Hydraulic Pump", distinct.ItemName);
    }

    [Fact]
    public async Task SearchByBothItemAndSupplier_returns_matching_pair()
    {
        using var scope = _provider!.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPurchaseRequisitionRepository>();

        var results = await repo.SearchItemSupplierMasterAsync(item: "ITM0001", supplierCode: "SUP000001");

        var pair = Assert.Single(results);
        Assert.Equal("SUP000001", pair.SupplierCode);
        Assert.Equal("ITM0001", pair.Item);
    }

    [Fact]
    public async Task SearchWithNoParameters_returns_empty()
    {
        using var scope = _provider!.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPurchaseRequisitionRepository>();

        var results = await repo.SearchItemSupplierMasterAsync(item: null, supplierCode: null);

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchWithNonExistentCode_returns_empty()
    {
        using var scope = _provider!.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPurchaseRequisitionRepository>();

        var results = await repo.SearchItemSupplierMasterAsync(supplierCode: "SUP999999", item: null);

        Assert.Empty(results);
    }

    [Fact]
    public async Task Results_do_not_contain_requisition_fields()
    {
        using var scope = _provider!.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPurchaseRequisitionRepository>();

        var results = await repo.SearchItemSupplierMasterAsync(supplierCode: "SUP000001", item: null);

        Assert.All(results, r =>
        {
            Assert.NotNull(r.SupplierCode);
            Assert.NotNull(r.SupplierName);
            Assert.NotNull(r.Item);
            Assert.NotNull(r.ItemName);
        });
        // Verify the type only has the four expected properties
        var properties = typeof(ItemSupplierMasterResult).GetProperties();
        Assert.Equal(4, properties.Length);
    }

    private async Task WriteJsonAsync(IEnumerable<PurchaseRequisitionImport> records)
    {
        var path = Path.Combine(_dataDir, "purchase.json");
        var json = JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
    }

    private static PurchaseRequisitionImport Requisition(
        string pr,
        string supplierCode,
        string item,
        string supplierName,
        string itemName) => new()
    {
        PurchaseRequisition = pr,
        SupplierCode = supplierCode,
        SupplierName = supplierName,
        Item = item,
        ItemName = itemName,
        Description = $"Procurement of {itemName} for maintenance operations.",
    };
}
