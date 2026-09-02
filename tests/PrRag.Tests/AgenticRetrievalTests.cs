using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using PrRag.Application.Abstractions;
using PrRag.Application.DTOs;
using Xunit;

namespace PrRag.Tests;

public class AgenticRetrievalTests : IAsyncLifetime
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
    public async Task Model_calls_exact_match_tool_and_answer_is_grounded()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.ToolCall = new FunctionCallContent(
            "call_1",
            "search_by_codes",
            new Dictionary<string, object?> { ["suppliers"] = new[] { "SUP000001" } });

        var response = await chat.AnswerAsync(new ChatRequest
        {
            Question = "what is requisition from supplier SUP000001?",
            TopK = 5,
            MinSimilarity = 0,
        });

        Assert.Equal(1, response.RetrievedCount);
        Assert.NotEmpty(response.Answer);
        Assert.Contains("SUP000001", chatClient.LastPrompt);
    }

    [Fact]
    public async Task Model_calls_semantic_tool_and_answer_is_grounded_above_threshold()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.ToolCall = new FunctionCallContent(
            "call_1",
            "search_semantic",
            new Dictionary<string, object?> { ["query"] = "hydraulic pump" });

        var response = await chat.AnswerAsync(new ChatRequest
        {
            Question = "tell me about the hydraulic pump",
            TopK = 5,
            MinSimilarity = 0.01,
        });

        Assert.True(response.RetrievedCount > 0);
        Assert.NotEmpty(response.Answer);
    }

    [Fact]
    public async Task Model_answers_without_retrieval()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.ToolCall = null;

        var response = await chat.AnswerAsync(new ChatRequest
        {
            Question = "hello, how are you?",
            TopK = 5,
            MinSimilarity = 0,
        });

        Assert.Equal(0, response.RetrievedCount);
        Assert.Equal(1, chatClient.CallCount);
    }

    [Fact]
    public async Task Full_history_carried_across_turns()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.ToolCall = new FunctionCallContent(
            "call_1",
            "search_by_codes",
            new Dictionary<string, object?> { ["suppliers"] = new[] { "SUP000002" } });

        var streamed = new List<string>();
        await foreach (var token in chat.StreamAsync(new ChatStreamRequest
        {
            Question = "give me details on SUP000002",
            TopK = 5,
            MinSimilarity = 0,
            Messages = new List<ChatMessageDto>
            {
                new() { Role = "user", Content = "I need info about Beta Components." },
                new() { Role = "assistant", Content = "I will fetch that supplier." },
            },
        }))
        {
            streamed.Add(token);
        }

        Assert.NotEmpty(streamed);

        var messages = chatClient.LastMessages;
        Assert.Contains(messages, m => m.Role == ChatRole.Tool);
        Assert.Contains(messages, m => m.Text == "I need info about Beta Components.");
        Assert.Contains(messages, m => m.Role == ChatRole.Assistant && m.Text == "I will fetch that supplier.");
    }

    [Fact]
    public async Task Semantic_search_rewrites_query_with_full_conversation()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();
        var rewriter = scope.ServiceProvider.GetRequiredService<FakeQueryRewriter>();

        chatClient.ToolCall = new FunctionCallContent(
            "call_1",
            "search_semantic",
            new Dictionary<string, object?> { ["query"] = "the other one we saw earlier" });

        var history = new List<ChatMessageDto>
        {
            new() { Role = "user", Content = "me mostra as PRs do fornecedor Acme" },
            new() { Role = "assistant", Content = "vou buscar isso" },
        };

        var streamed = new List<string>();
        await foreach (var token in chat.StreamAsync(new ChatStreamRequest
        {
            Question = "e daquele outro que a gente viu antes",
            TopK = 5,
            MinSimilarity = 0.01,
            Messages = history,
        }))
        {
            streamed.Add(token);
        }

        Assert.NotEmpty(streamed);
        Assert.Equal(1, rewriter.CallCount);
        Assert.Equal("the other one we saw earlier", rewriter.LastQuestion);

        var conversation = rewriter.LastConversation!;
        Assert.Contains(conversation, m => m.Role == ChatRole.User && m.Text == "me mostra as PRs do fornecedor Acme");
        Assert.Contains(conversation, m => m.Role == ChatRole.User && m.Text == "e daquele outro que a gente viu antes");
        Assert.Contains(conversation, m => m.Role == ChatRole.Assistant && m.Text == "vou buscar isso");
    }

    [Fact]
    public async Task Exact_match_tool_does_not_invoke_query_rewriter()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();
        var rewriter = scope.ServiceProvider.GetRequiredService<FakeQueryRewriter>();

        chatClient.ToolCall = new FunctionCallContent(
            "call_1",
            "search_by_codes",
            new Dictionary<string, object?> { ["suppliers"] = new[] { "SUP000001" } });

        var response = await chat.AnswerAsync(new ChatRequest
        {
            Question = "what is requisition from supplier SUP000001?",
            TopK = 5,
            MinSimilarity = 0,
        });

        Assert.Equal(1, response.RetrievedCount);
        Assert.Equal(0, rewriter.CallCount);
    }

    private async Task WriteJsonAsync(IEnumerable<PurchaseRequisitionImport> records)
    {
        var path = Path.Combine(_dataDir, "purchase.json");
        var json = JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
    }
}
