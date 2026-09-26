using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using PrRag.Application.Abstractions;
using PrRag.Application.DTOs;
using Xunit;
using PrRag.Application.Services.Agents;

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
            await TestDatabase.DropDatabaseAsync(_dbName);
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

        chatClient.AutoHandoff = true;
        chatClient.ScriptFor(ToolNames.SearchByCodes, new FunctionCallContent(
            "call_1",
            ToolNames.SearchByCodes,
            new Dictionary<string, object?> { ["suppliers"] = new[] { "SUP000001" } }));

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

        chatClient.AutoHandoff = true;
        chatClient.ScriptFor(ToolNames.SearchSemantic, new FunctionCallContent(
            "call_1",
            ToolNames.SearchSemantic,
            new Dictionary<string, object?> { ["query"] = "hydraulic pump" }));

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

    /// <summary>
    /// The answer is the run's answer, not the transcript that produced it.
    ///
    /// <para>
    /// A multi-turn run reports the request back as outputs, the earlier
    /// question and answer included, and folding those together is what made a
    /// reply arrive as its own history replayed ahead of it. This fails if the
    /// run's messages are concatenated back into the answer again, which is the
    /// only place that could reintroduce it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Answer_is_the_new_reply_and_not_the_replayed_transcript()
    {
        var sessionId = Guid.NewGuid().ToString("N");
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.AutoHandoff = true;
        chatClient.ScriptFor(ToolNames.SearchByCodes, new FunctionCallContent(
            "call_1",
            ToolNames.SearchByCodes,
            new Dictionary<string, object?> { ["suppliers"] = new[] { "SUP000002" } }));

        var first = await chat.AnswerAsync(new ChatRequest
        {
            SessionId = sessionId,
            Question = "give me details on SUP000002",
            TopK = 5,
            MinSimilarity = 0,
        });
        Assert.Equal("fake answer", first.Answer);

        using var next = _provider.CreateScope();
        var nextChat = next.ServiceProvider.GetRequiredService<IChatService>();
        var nextClient = next.ServiceProvider.GetRequiredService<FakeChatClient>();
        nextClient.AutoHandoff = true;

        var second = await nextChat.AnswerAsync(new ChatRequest
        {
            SessionId = sessionId,
            Question = "tell me more about that supplier",
            TopK = 5,
            MinSimilarity = 0,
        });

        // The earlier turn must not reappear in the answer.
        Assert.Equal("fake answer", second.Answer);
        Assert.DoesNotContain("give me details on SUP000002", second.Answer);
    }

    [Fact]
    public async Task Full_history_carried_across_turns()
    {
        var sessionId = Guid.NewGuid().ToString("N");
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.AutoHandoff = true;
        chatClient.ScriptFor(ToolNames.SearchByCodes, new FunctionCallContent(
            "call_1",
            ToolNames.SearchByCodes,
            new Dictionary<string, object?> { ["suppliers"] = new[] { "SUP000002" } }));

        var first = await chat.AnswerAsync(new ChatRequest
        {
            SessionId = sessionId,
            Question = "give me details on SUP000002",
            TopK = 5,
            MinSimilarity = 0,
        });
        Assert.Equal(1, first.RetrievedCount);

        // Second turn on the same session: history is carried server-side, so
        // the client does not resend it.
        var streamed = new List<string>();
        await foreach (var token in chat.StreamAsync(new ChatStreamRequest
        {
            SessionId = sessionId,
            Question = "tell me more about that supplier",
            TopK = 5,
            MinSimilarity = 0,
        }))
        {
            streamed.Add(token);
        }

        Assert.NotEmpty(streamed);

        // The conversation is carried as text turns, never as tool traffic: no
        // half of a call/result pair is replayable. See ChatService.RecordTurn.
        var messages = chatClient.LastMessages;
        Assert.DoesNotContain(messages, m => m.Contents.Any(c => c is FunctionCallContent or FunctionResultContent));
        Assert.Contains(messages, m => m.Text == "give me details on SUP000002");
        Assert.Contains(messages, m => m.Text == "fake answer");
        Assert.Contains(messages, m => m.Text == "tell me more about that supplier");
    }

    /// <summary>
    /// MAF's agent response carries the request it was given as well as the
    /// reply, so recording the response back into the conversation re-records the
    /// question — and on a later turn, every earlier turn. The history then grows
    /// geometrically and the model answers a follow-up by repeating itself.
    /// </summary>
    [Fact]
    public async Task Recording_a_turn_does_not_re_record_the_earlier_ones()
    {
        var sessionId = Guid.NewGuid().ToString("N");
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();
        chatClient.EchoRequest = true;

        await chat.AnswerAsync(new ChatRequest
        {
            SessionId = sessionId,
            Question = "first question",
            TopK = 5,
            MinSimilarity = 0,
        });

        await chat.AnswerAsync(new ChatRequest
        {
            SessionId = sessionId,
            Question = "second question",
            TopK = 5,
            MinSimilarity = 0,
        });

        await chat.AnswerAsync(new ChatRequest
        {
            SessionId = sessionId,
            Question = "third question",
            TopK = 5,
            MinSimilarity = 0,
        });

        var state = scope.ServiceProvider
            .GetRequiredService<IAgentSessionStore>()
            .GetOrCreate(sessionId);

        var questions = state.History
            .Where(m => m.Role == ChatRole.User)
            .Select(m => m.Text)
            .ToList();

        Assert.Equal(["first question", "second question", "third question"], questions);
    }

    private async Task WriteJsonAsync(IEnumerable<PurchaseRequisitionImport> records)
    {
        var path = Path.Combine(_dataDir, "purchase.json");
        var json = JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
    }
}
