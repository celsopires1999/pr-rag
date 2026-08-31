using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PrRag.Application.Abstractions;
using PrRag.Application.DTOs;
using PrRag.Infrastructure.Persistence;
using Xunit;

namespace PrRag.Tests;

public class QueryRewriterRetrievalTests : IAsyncLifetime
{
    private static string ConnectionTemplate =>
        Environment.GetEnvironmentVariable("TEST_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Username=prrag;Password=prrag";

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
            new PurchaseRequisitionImport
            {
                PurchaseRequisition = "PR00000001",
                SupplierCode = "SUP000001",
                SupplierName = "Acme Industrial Supply",
                Item = "ITM0001",
                ItemName = "Hydraulic Pump",
                Description = "Procurement of Hydraulic Pump for maintenance operations.",
            },
            new PurchaseRequisitionImport
            {
                PurchaseRequisition = "PR00000002",
                SupplierCode = "SUP000002",
                SupplierName = "Beta Components Ltd",
                Item = "ITM0002",
                ItemName = "Ball Bearings",
                Description = "Procurement of Ball Bearings for maintenance operations.",
            },
        };
        await WriteJsonAsync(records);

        using var scope2 = _provider.CreateScope();
        var service = scope2.ServiceProvider.GetRequiredService<IIngestionService>();
        await service.IngestAsync();
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
    public async Task Rewriter_runs_when_vector_search_is_needed()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var rewriter = scope.ServiceProvider.GetRequiredService<FakeQueryRewriter>();

        await chat.AnswerAsync(new ChatRequest
        {
            Question = "acme hydraulic pump",
            TopK = 5,
            MinSimilarity = 0,
        });

        Assert.True(rewriter.CallCount > 0);
        Assert.Equal("acme hydraulic pump", rewriter.LastQuestion);
    }

    [Fact]
    public async Task Rewriter_skipped_when_codes_fill_topK()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var rewriter = scope.ServiceProvider.GetRequiredService<FakeQueryRewriter>();

        var response = await chat.AnswerAsync(new ChatRequest
        {
            Question = "SUP000001 SUP000002",
            TopK = 2,
            MinSimilarity = 0,
        });

        Assert.Equal(0, rewriter.CallCount);
        Assert.Equal(2, response.RetrievedCount);
    }

    [Fact]
    public async Task Original_question_used_for_final_answer()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        var original = "acme hydraulic pump requisitions";
        await chat.AnswerAsync(new ChatRequest
        {
            Question = original,
            TopK = 5,
            MinSimilarity = 0.01,
        });

        Assert.Contains(original, chatClient.LastPrompt);
    }

    private async Task WriteJsonAsync(IEnumerable<PurchaseRequisitionImport> records)
    {
        var path = Path.Combine(_dataDir, "purchase.json");
        var json = JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
    }
}
