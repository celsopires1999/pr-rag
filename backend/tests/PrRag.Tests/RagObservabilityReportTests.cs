using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using PrRag.Application.Abstractions;
using PrRag.Application.Domain;
using PrRag.Application.DTOs;
using Xunit;
using PrRag.Application.Services.Agents;

namespace PrRag.Tests;

public class RagObservabilityReportTests : IAsyncLifetime
{
    private static string ConnectionTemplate => TestDatabase.ConnectionStringTemplate;

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
            await TestDatabase.DropDatabaseAsync(_dbName);
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

        chatClient.AutoHandoff = true;
        chatClient.ScriptFor(ToolNames.SearchSemantic, new FunctionCallContent(
            "call_1",
            ToolNames.SearchSemantic,
            new Dictionary<string, object?> { ["query"] = "acme hydraulic pump" }));

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
        Assert.Equal("acme hydraulic pump", report.RewrittenQuery);

        var toolCall = Assert.Single(report.ToolCalls);
        Assert.Equal("search_semantic", toolCall.Name);
        Assert.Equal("acme hydraulic pump", ((JsonElement)toolCall.Arguments["query"]!).GetString());
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

        // Nothing was retrieved *and* nothing was searched. Before
        // RetrievalAttempted existed this was indistinguishable from a search that
        // came back empty, which is the case the live gate tripped over: a turn
        // that narrated "Executed the search" and called no tool looked exactly
        // like this one in the report.
        Assert.False(report.RetrievalAttempted);
        Assert.Equal(0, report.RetrievedCount);
        Assert.Empty(report.RetrievedItems);
        Assert.Empty(report.ToolCalls);
        Assert.Equal(response.Answer, report.Answer);
        Assert.Equal("SUP999999", report.Question);
        Assert.Equal(5, report.TopK);
        Assert.False(report.TopKFromRequest);
        Assert.Equal(0.7, report.MinSimilarity);
        Assert.False(report.MinSimilarityFromRequest);
    }

    [Fact]
    public async Task Report_records_each_tool_invocation_in_order_with_arguments()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.AutoHandoff = true;
        chatClient.ScriptFor(ToolNames.SearchByCodes, new FunctionCallContent(
            "call_1",
            ToolNames.SearchByCodes,
            new Dictionary<string, object?> { ["items"] = new[] { "ITM0001" } }));
        chatClient.AutoHandoff = true;
        chatClient.ScriptFor(ToolNames.SearchSemantic, new FunctionCallContent(
            "call_2",
            ToolNames.SearchSemantic,
            new Dictionary<string, object?> { ["query"] = "hydraulic pump" }));

        var response = await chat.AnswerAsync(new ChatRequest { Question = "find the pump" });

        var report = await ReadLastReportAsync(ReportsDir);

        Assert.True(response.RetrievedCount > 0);
        Assert.Equal(2, report.ToolCalls.Count);
        Assert.Equal("search_by_codes", report.ToolCalls[0].Name);
        var items = (JsonElement)report.ToolCalls[0].Arguments["items"]!;
        Assert.Equal("ITM0001", items[0].GetString());
        Assert.Equal("search_semantic", report.ToolCalls[1].Name);
        Assert.Equal("hydraulic pump", ((JsonElement)report.ToolCalls[1].Arguments["query"]!).GetString());
    }
    /// <summary>
    /// The other half of the pair: a search that ran and found nothing.
    /// </summary>
    /// <remarks>
    /// This is the case that shares every other observable with
    /// <see cref="Report_written_for_no_context_fallback"/> — zero rows, fallback
    /// set — and the two together are why <c>UsedNoContextFallback</c> alone
    /// cannot answer "did we look?".
    /// </remarks>
    [Fact]
    public async Task A_search_that_found_nothing_is_distinguishable_from_never_searching()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.AutoHandoff = true;
        chatClient.ScriptFor(ToolNames.SearchByCodes, new FunctionCallContent(
            "call_1",
            ToolNames.SearchByCodes,
            new Dictionary<string, object?> { ["suppliers"] = new[] { "SUP999999" } }));

        var response = await chat.AnswerAsync(new ChatRequest { Question = "anything for SUP999999?" });

        var report = await ReadLastReportAsync(ReportsDir);

        Assert.Equal(0, response.RetrievedCount);
        Assert.True(report.UsedNoContextFallback);
        Assert.True(report.RetrievalAttempted);
        Assert.Single(report.ToolCalls);
        Assert.Equal(ToolNames.SearchByCodes, report.ToolCalls[0].Name);
    }

    /// <summary>
    /// The report must not be able to claim rows it does not carry, or a
    /// retrieval that never happened.
    /// </summary>
    [Fact]
    public async Task Retrieved_count_agrees_with_the_items_and_implies_a_retrieval_attempt()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.AutoHandoff = true;
        chatClient.ScriptFor(ToolNames.SearchByCodes, new FunctionCallContent(
            "call_1",
            ToolNames.SearchByCodes,
            new Dictionary<string, object?> { ["items"] = new[] { "ITM0001" } }));

        var response = await chat.AnswerAsync(new ChatRequest { Question = "find the pump" });

        var report = await ReadLastReportAsync(ReportsDir);

        Assert.True(report.RetrievedCount > 0);
        Assert.True(report.RetrievalAttempted);
        Assert.Equal(report.RetrievedCount, report.RetrievedItems.Count);
        Assert.Equal(response.RetrievedCount, report.RetrievedCount);
    }

    /// <summary>
    /// A write tool is not a retrieval. A turn that only staged a draft retrieves
    /// nothing, and must not be recorded as having consulted the data.
    /// </summary>
    [Fact]
    public async Task A_write_only_turn_is_not_recorded_as_a_retrieval()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.AutoHandoff = false;
        chatClient.ScriptFor(ToolNames.CreateRequisitionDraft, new FunctionCallContent(
            "call_1",
            ToolNames.CreateRequisitionDraft,
            new Dictionary<string, object?>
            {
                ["supplierCode"] = "SUP000001",
                ["item"] = "ITM0001",
                ["description"] = "Pump",
                ["quantity"] = 1,
                ["date"] = "2026-01-15",
                ["requester"] = "Jane Doe",
            }));

        await chat.AnswerAsync(new ChatRequest { Question = "draft a requisition for ITM0001" });

        var report = await ReadLastReportAsync(ReportsDir);

        Assert.Single(report.ToolCalls);
        Assert.Equal(ToolNames.CreateRequisitionDraft, report.ToolCalls[0].Name);
        Assert.False(report.RetrievalAttempted);
        Assert.Equal(0, report.RetrievedCount);
    }

    /// <summary>
    /// A run that produced no text did not succeed, and must not look like it did.
    /// </summary>
    /// <remarks>
    /// The framework completes a run whose chat call threw — a rejected key, a
    /// provider outage — and surfaces an empty run instead of an exception. An
    /// empty <c>FakeChatClient.Answer</c> is exactly that shape. Before this, the
    /// caller received 200 with a blank answer and the report claimed a no-context
    /// fallback that was never sent, so a dead provider was indistinguishable from
    /// a quiet turn and the live hygiene gate reported it as clean.
    /// </remarks>
    [Fact]
    public async Task A_turn_that_produced_no_answer_fails_rather_than_returning_an_empty_success()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.Answer = string.Empty;

        var ex = await Assert.ThrowsAsync<ChatTurnFailedException>(
            () => chat.AnswerAsync(new ChatRequest { Question = "acme hydraulic pump" }));

        Assert.Contains("without producing any answer text", ex.Message);
    }

    /// <summary>
    /// The report for a failed turn must not claim the caller was given a fallback
    /// answer, because they were not.
    /// </summary>
    /// <remarks>
    /// The report is still written on the way out, so this is the only artefact
    /// left behind by a failed turn and it is what the live gate reads. Leaving
    /// <c>UsedNoContextFallback</c> keyed only on the retrieval count is what let
    /// the outage look like an ordinary empty result.
    /// </remarks>
    [Fact]
    public async Task A_failed_turn_does_not_record_a_no_context_fallback()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.Answer = string.Empty;

        await Assert.ThrowsAsync<ChatTurnFailedException>(
            () => chat.AnswerAsync(new ChatRequest { Question = "acme hydraulic pump" }));

        var report = await ReadLastReportAsync(ReportsDir);

        Assert.Equal("acme hydraulic pump", report.Question);
        Assert.Equal(string.Empty, report.Answer);
        Assert.Equal(0, report.RetrievedCount);
        Assert.False(report.RetrievalAttempted);

        // The retrieval count is zero here for the same reason it is zero on a
        // genuine no-context turn, which is exactly why the flag needed the answer
        // to agree with it.
        Assert.False(report.UsedNoContextFallback);
    }

    /// <summary>
    /// A failed turn must not be replayed into the rest of the conversation.
    /// </summary>
    /// <remarks>
    /// Recording a question paired with an empty answer would leave the session
    /// holding a broken turn, and every later turn would be built on it.
    /// </remarks>
    [Fact]
    public async Task A_failed_turn_is_not_recorded_in_the_conversation()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.Answer = string.Empty;
        await Assert.ThrowsAsync<ChatTurnFailedException>(
            () => chat.AnswerAsync(new ChatRequest { Question = "the question that failed" }));

        // A later turn must not see the failed one replayed back at it.
        chatClient.Answer = "a real answer";
        await chat.AnswerAsync(new ChatRequest { Question = "the next question" });

        var replayed = chatClient.LastMessages
            .Select(m => m.Contents.OfType<TextContent>().FirstOrDefault()?.Text)
            .Where(t => t is not null)
            .ToList();

        Assert.DoesNotContain("the question that failed", replayed);
        Assert.Contains("the next question", replayed);
    }

    /// <summary>
    /// A turn that did produce an answer keeps the fallback flag on the retrieval
    /// condition, so the fix above did not quietly disable it.
    /// </summary>
    [Fact]
    public async Task A_real_empty_retrieval_still_records_the_fallback()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.Answer = "I could not find any matching requisitions.";

        var response = await chat.AnswerAsync(new ChatRequest { Question = "SUP999999" });
        var report = await ReadLastReportAsync(ReportsDir);

        Assert.NotEmpty(response.Answer);
        Assert.Equal(0, report.RetrievedCount);
        Assert.True(report.UsedNoContextFallback);
    }
}
