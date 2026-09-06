using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using PrRag.Application.Abstractions;
using PrRag.Application.DTOs;
using Xunit;

namespace PrRag.Tests;

public class ItemSupplierCombinationTests : IAsyncLifetime
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

    private string RequisitionsDir => Path.Combine(_dataDir, "requisitions");

    private static string LastToolResult(FakeChatClient chatClient)
    {
        var toolMessage = chatClient.LastMessages.Last(m => m.Role == ChatRole.Tool);
        var content = toolMessage.Contents.OfType<FunctionResultContent>().Last();
        return content.Result?.ToString() ?? string.Empty;
    }

    [Fact]
    public async Task ExistsItemSupplierCombination_returns_true_only_for_present_combinations()
    {
        using var scope = _provider!.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPurchaseRequisitionRepository>();

        Assert.True(await repo.ExistsItemSupplierCombinationAsync("ITM0001", "SUP000001"));

        // Item exists with a different supplier only — combination absent.
        Assert.False(await repo.ExistsItemSupplierCombinationAsync("ITM0001", "SUP000002"));
        // Supplier exists with a different item only — combination absent.
        Assert.False(await repo.ExistsItemSupplierCombinationAsync("ITM0003", "SUP000001"));
        // Both codes exist separately but never together — combination absent.
        Assert.False(await repo.ExistsItemSupplierCombinationAsync("ITM0002", "SUP000002"));
        // Entirely unknown codes.
        Assert.False(await repo.ExistsItemSupplierCombinationAsync("ITM9000", "SUP900000"));
    }

    [Fact]
    public async Task Create_requisition_is_refused_when_codes_exist_separately_but_never_together()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.ScriptedToolCalls.Add(new FunctionCallContent(
            "call_create",
            "create_requisition",
            new Dictionary<string, object?>
            {
                ["supplierCode"] = "SUP000002",
                ["item"] = "ITM0002",
                ["description"] = "Ball bearings for maintenance.",
                ["quantity"] = 3m,
                ["date"] = "2026-10-01",
                ["requester"] = "Ana Souza",
            }));

        await chat.AnswerAsync(new ChatRequest
        {
            Question = "persist a requisition",
            TopK = 5,
            MinSimilarity = 0,
        });

        Assert.Contains("not registered for that item", LastToolResult(chatClient));
        Assert.False(Directory.Exists(RequisitionsDir) && Directory.GetFiles(RequisitionsDir, "*.json").Length > 0);
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