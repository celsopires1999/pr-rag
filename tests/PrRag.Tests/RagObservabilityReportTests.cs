using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using PrRag.Application.Abstractions;
using PrRag.Application.DTOs;
using Xunit;

namespace PrRag.Tests;

public class RagObservabilityReportTests : IAsyncLifetime
{
    private static string ConnectionTemplate =>
        Environment.GetEnvironmentVariable("TEST_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Username=prrag;Password=prrag";

    private readonly string _dbName = $"prrag_test_{Guid.NewGuid():N}";
    private string _connectionString = null!;
    private ServiceProvider? _provider;
    private string _dataDir = null!;

    private static readonly PurchaseRequisitionImport[] Seed =
    {
        new()
        {
            PurchaseRequisition = "PR00000001",
            SupplierCode = "SUP000001",
            SupplierName = "Acme Industrial Supply",
            Item = "ITM0001",
            ItemName = "Hydraulic Pump",
            Description = "Procurement of Hydraulic Pump for maintenance operations.",
        },
        new()
        {
            PurchaseRequisition = "PR00000002",
            SupplierCode = "SUP000002",
            SupplierName = "Beta Components Ltd",
            Item = "ITM0002",
            ItemName = "Ball Bearings",
            Description = "Procurement of Ball Bearings for maintenance operations.",
        },
    };

    public async Task InitializeAsync()
    {
        _connectionString = $"{ConnectionTemplate};Database={_dbName}";

        (_provider, _, _dataDir) = IntegrationServiceFactory.Create(_connectionString);

        await TestDatabase.MigrateAndReloadTypesAsync(_provider);

        var path = Path.Combine(_dataDir, "purchase.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(Seed, new JsonSerializerOptions { WriteIndented = true }));

        using var scope = _provider.CreateScope();
        var ingestion = scope.ServiceProvider.GetRequiredService<IIngestionService>();
        await ingestion.IngestAsync();
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

    private string ReportsDir => Path.Combine(_dataDir, "reports");

    private static async Task<RagQueryReport> ReadLastReportAsync(string reportsDir)
    {
        var file = Directory.GetFiles(reportsDir, "*.json")
            .OrderByDescending(f => f)
            .First();
        var json = await File.ReadAllTextAsync(file);
        return JsonSerializer.Deserialize<RagQueryReport>(json)!;
    }

    [Fact]
    public async Task Report_written_with_question_parameters_and_answer()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.ToolCall = new FunctionCallContent(
            "call_1",
            "search_semantic",
            new Dictionary<string, object?> { ["query"] = "acme hydraulic pump" });

        var response = await chat.AnswerAsync(new ChatRequest
        {
            Question = "acme hydraulic pump",
            MinSimilarity = 0.01,
        });

        var report = await ReadLastReportAsync(ReportsDir);

        Assert.NotEmpty(response.Answer);
        Assert.Equal("acme hydraulic pump", report.Question);
        Assert.Equal(5, report.TopK);
        Assert.False(report.TopKFromRequest);
        Assert.Equal(0.01, report.MinSimilarity);
        Assert.True(report.MinSimilarityFromRequest);
        Assert.True(report.RetrievedCount > 0);
        Assert.Equal(response.Answer, report.Answer);
    }

    [Fact]
    public async Task Report_written_for_no_context_fallback()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();

        var response = await chat.AnswerAsync(new ChatRequest
        {
            Question = "SUP999999",
        });

        var report = await ReadLastReportAsync(ReportsDir);

        Assert.Equal(0, response.RetrievedCount);
        Assert.True(report.UsedNoContextFallback);
        Assert.Equal(0, report.RetrievedCount);
        Assert.Empty(report.RetrievedItems);
        Assert.Equal(response.Answer, report.Answer);
        Assert.Equal("SUP999999", report.Question);
        Assert.Equal(5, report.TopK);
        Assert.False(report.TopKFromRequest);
        Assert.Equal(0.7, report.MinSimilarity);
        Assert.False(report.MinSimilarityFromRequest);
    }
}
