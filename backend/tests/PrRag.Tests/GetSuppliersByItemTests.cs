using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using PrRag.Application.Abstractions;
using PrRag.Application.DTOs;
using PrRag.Application.Services.Agents;
using Xunit;

namespace PrRag.Tests;

public class GetSuppliersByItemTests : IAsyncLifetime
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
            Requisition("PR00000001", "SUP000001", "Acme Industrial Supply", "ITM0001", "Hydraulic Oil"),
            Requisition("PR00000002", "SUP000002", "Beta Components Ltd", "ITM0001", "Hydraulic Oil"),
            Requisition("PR00000003", "SUP000001", "Acme Industrial Supply", "ITM0001", "Hydraulic Oil"),
            Requisition("PR00000004", "SUP000003", "Gamma Tools", "ITM0001", "Hydraulic Oil"),
            Requisition("PR00000005", "SUP000004", "Delta Parts", "ITM0002", "Safety Gloves"),
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
    public async Task Returns_distinct_suppliers_for_an_item_with_multiple_suppliers()
    {
        using var scope = _provider!.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPurchaseRequisitionRepository>();

        var suppliers = await repo.GetSuppliersByItemAsync("ITM0001");

        Assert.Equal(3, suppliers.Count);
        Assert.Contains(suppliers, s => s is { SupplierCode: "SUP000001", SupplierName: "Acme Industrial Supply" });
        Assert.Contains(suppliers, s => s is { SupplierCode: "SUP000002", SupplierName: "Beta Components Ltd" });
        Assert.Contains(suppliers, s => s is { SupplierCode: "SUP000003", SupplierName: "Gamma Tools" });
    }

    [Fact]
    public async Task Collapses_duplicate_item_and_supplier_rows_to_one_entry()
    {
        using var scope = _provider!.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPurchaseRequisitionRepository>();

        var suppliers = await repo.GetSuppliersByItemAsync("ITM0001");

        Assert.Single(suppliers, s => s.SupplierCode == "SUP000001");
    }

    [Fact]
    public async Task Returns_empty_list_for_an_unknown_item()
    {
        using var scope = _provider!.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPurchaseRequisitionRepository>();

        var suppliers = await repo.GetSuppliersByItemAsync("ITM9999");

        Assert.Empty(suppliers);
    }

    [Fact]
    public async Task Get_suppliers_by_item_tool_returns_the_distinct_supplier_list()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.ScriptedToolCalls.Add(new FunctionCallContent(
            "call_get_suppliers",
            ToolNames.GetSuppliersByItem,
            new Dictionary<string, object?>
            {
                ["item"] = "ITM0001",
            }));

        await chat.AnswerAsync(new ChatRequest
        {
            Question = "Which suppliers provided the item Hydraulic Oil?",
            TopK = 5,
            MinSimilarity = 0,
        });

        var toolMessage = chatClient.LastMessages.Last(m => m.Role == ChatRole.Tool);
        var content = toolMessage.Contents.OfType<FunctionResultContent>().Last();
        var json = JsonSerializer.Serialize(content.Result);

        // Counted object, camelCase keys — the model-facing result contract.
        using var document = JsonDocument.Parse(json);
        var result = document.RootElement;

        Assert.Equal(3, result.GetProperty("count").GetInt32());

        var suppliers = result.GetProperty("suppliers")
            .EnumerateArray()
            .ToDictionary(
                s => s.GetProperty("supplierCode").GetString()!,
                s => s.GetProperty("supplierName").GetString()!);

        Assert.Equal("Acme Industrial Supply", suppliers["SUP000001"]);
        Assert.Equal("Beta Components Ltd", suppliers["SUP000002"]);
        Assert.Equal("Gamma Tools", suppliers["SUP000003"]);
    }

    [Fact]
    public async Task Get_suppliers_by_item_tool_reports_zero_count_for_an_unknown_item()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.ScriptedToolCalls.Add(new FunctionCallContent(
            "call_get_suppliers",
            ToolNames.GetSuppliersByItem,
            new Dictionary<string, object?>
            {
                ["item"] = "ITM9999",
            }));

        await chat.AnswerAsync(new ChatRequest
        {
            Question = "Which suppliers provided the item ITM9999?",
            TopK = 5,
            MinSimilarity = 0,
        });

        var toolMessage = chatClient.LastMessages.Last(m => m.Role == ChatRole.Tool);
        var content = toolMessage.Contents.OfType<FunctionResultContent>().Last();

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(content.Result));
        var result = document.RootElement;

        Assert.Equal(0, result.GetProperty("count").GetInt32());
        Assert.Empty(result.GetProperty("suppliers").EnumerateArray());
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
        string supplierName,
        string item,
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