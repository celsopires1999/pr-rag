using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using PrRag.Application.Abstractions;
using PrRag.Application.DTOs;
using PrRag.Application.Services.Agents;
using PrRag.Application.Services.Agents.Specialists;
using Xunit;

namespace PrRag.Tests;

/// <summary>
/// The graph's shape, asserted where it is actually load-bearing: on the tools
/// each agent is offered at run time, on the answer a participant hands back,
/// and on the report a routed turn produces.
/// </summary>
/// <remarks>
/// These are deliberately behavioural. Asserting the catalog's definitions would
/// pass even if the composer wired the tools onto the wrong agent, because the
/// definitions and the composition are separate places. The fake records the
/// tools each request was issued with, so these tests read what the model
/// actually saw.
/// </remarks>
public class AgentGraphTopologyTests : IAsyncLifetime
{
    private static readonly string[] RetrievalTools =
        [ToolNames.SearchByCodes, ToolNames.SearchSemantic, ToolNames.GetSuppliersByItem];

    private static readonly string[] OrchestratorTools =
    [
        ToolNames.CreateRequisitionDraft,
        ToolNames.ConfirmRequisitionDraft,
        ToolNames.CreateRequisition,
        ToolNames.ActivateSkill,
    ];

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

        await WriteJsonAsync(
        [
            new PurchaseRequisitionImport
            {
                PurchaseRequisition = "PR00000001",
                SupplierCode = "SUP000001",
                SupplierName = "Acme Industrial Supply",
                Item = "ITM0001",
                ItemName = "Hydraulic Pump",
                Description = "Procurement of Hydraulic Pump for maintenance operations.",
            },
        ]);

        using var scope = _provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IIngestionService>().IngestAsync();
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

    /// <summary>
    /// A routed turn must reach the retrieval agent holding the read tools, and
    /// that agent must not be offered a single write tool.
    /// </summary>
    [Fact]
    public async Task A_routed_turn_is_offered_only_the_retrieval_tools()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.AutoHandoff = true;
        chatClient.ToolCall = new FunctionCallContent(
            "call_1",
            ToolNames.SearchByCodes,
            new Dictionary<string, object?> { ["suppliers"] = new[] { "SUP000001" } });

        await chat.AnswerAsync(new ChatRequest
        {
            Question = "what is requisition from supplier SUP000001?",
            TopK = 5,
            MinSimilarity = 0,
        });

        var offered = LastOffered(chatClient);
        Assert.Equal(RetrievalTools.OrderBy(n => n, StringComparer.Ordinal), offered.OrderBy(n => n, StringComparer.Ordinal));

        // The write tools live on the orchestrator. If routing ever handed the
        // participant the combined list, this is the assertion that catches it.
        Assert.DoesNotContain(ToolNames.CreateRequisition, offered);
        Assert.DoesNotContain(ToolNames.CreateRequisitionDraft, offered);
    }

    /// <summary>
    /// The converse: a creation turn stays on the orchestrator, which must hold
    /// the write tools and must not hold the read tools.
    /// </summary>
    [Fact]
    public async Task An_unrouted_creation_turn_is_offered_only_the_orchestrator_tools()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        // No handoff: this turn is about the orchestrator's own tools. With
        // AutoHandoff on, the fake would route the turn to the participant and
        // the assertion below would read the wrong agent's tool set.
        chatClient.AutoHandoff = false;
        chatClient.ToolCall = new FunctionCallContent(
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
            });

        await chat.AnswerAsync(new ChatRequest
        {
            Question = "create a purchase requisition for ITM0001 from SUP000001",
            TopK = 5,
            MinSimilarity = 0,
        });

        var offered = LastOffered(chatClient);
        Assert.Equal(OrchestratorTools.OrderBy(n => n, StringComparer.Ordinal), offered.OrderBy(n => n, StringComparer.Ordinal));
        Assert.DoesNotContain(ToolNames.SearchByCodes, offered);
        Assert.DoesNotContain(ToolNames.GetSuppliersByItem, offered);
    }

    /// <summary>
    /// Across the two agents the offered sets must be exactly the seven wire
    /// names, partitioned. A tool on neither agent is unreachable; a tool on both
    /// gives the model two copies.
    /// </summary>
    [Fact]
    public async Task The_agents_partition_the_seven_tool_names_between_them()
    {
        var routed = await OfferedToolsForAsync(handoff: true);
        var unrouted = await OfferedToolsForAsync(handoff: false);

        var union = routed.Concat(unrouted).Distinct(StringComparer.Ordinal).ToList();

        Assert.Equal(7, union.Count);
        Assert.Equal(
            routed.Intersect(unrouted, StringComparer.Ordinal),
            Array.Empty<string>());
    }

    /// <summary>
    /// The participant's answer is the answer. A workflow that wrapped it in the
    /// orchestrator's own prose would still look correct to a reader, so this
    /// asserts the text is byte-identical to what the participant produced.
    /// </summary>
    [Fact]
    public async Task A_participants_answer_reaches_the_caller_unparaphrased()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        const string SpecialistAnswer = "PR00000001 from Acme Industrial Supply.";

        chatClient.AutoHandoff = true;
        chatClient.ToolCall = new FunctionCallContent(
            "call_1",
            ToolNames.SearchByCodes,
            new Dictionary<string, object?> { ["suppliers"] = new[] { "SUP000001" } });
        chatClient.Answer = SpecialistAnswer;

        var response = await chat.AnswerAsync(new ChatRequest
        {
            Question = "what is requisition from supplier SUP000001?",
            TopK = 5,
            MinSimilarity = 0,
        });

        Assert.Equal(SpecialistAnswer, response.Answer);
    }

    /// <summary>
    /// A finished participant returns control: the next turn is served by the
    /// orchestrator again, and it is the orchestrator's tool set that is on
    /// offer. Without this, a graph that trapped the conversation in the
    /// participant would still pass every routing test above.
    /// </summary>
    [Fact]
    public async Task A_finished_participant_returns_control_to_the_orchestrator()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.AutoHandoff = true;
        chatClient.ToolCall = new FunctionCallContent(
            "call_1",
            ToolNames.SearchByCodes,
            new Dictionary<string, object?> { ["suppliers"] = new[] { "SUP000001" } });
        chatClient.Answer = "retrieved";

        await chat.AnswerAsync(new ChatRequest
        {
            Question = "what is requisition from supplier SUP000001?",
            TopK = 5,
            MinSimilarity = 0,
            SessionId = "return-control",
        });

        // Second turn. AutoHandoff is cleared so the orchestrator is the one
        // that answers: leaving it on would route the follow-up straight back to
        // the participant and the assertion below could not tell the two apart.
        chatClient.AutoHandoff = false;

        var second = await chat.AnswerAsync(new ChatRequest
        {
            Question = "thanks, now create a purchase requisition for ITM0001",
            TopK = 5,
            MinSimilarity = 0,
            SessionId = "return-control",
        });

        Assert.NotEmpty(second.Answer);
        var offered = LastOffered(chatClient);
        Assert.Contains(ToolNames.CreateRequisitionDraft, offered);
        Assert.DoesNotContain(ToolNames.SearchByCodes, offered);
    }

    /// <summary>
    /// Streaming must actually emit. The rest of this suite only ever reads a
    /// completed answer, so a regression that left the SSE channel silent would
    /// pass all of it.
    /// </summary>
    [Fact]
    public async Task The_graph_emits_streaming_response_updates()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        const string Answer = "streamed answer for SUP000001";

        chatClient.AutoHandoff = true;
        chatClient.Answer = Answer;
        chatClient.ToolCall = new FunctionCallContent(
            "call_1",
            ToolNames.SearchByCodes,
            new Dictionary<string, object?> { ["suppliers"] = new[] { "SUP000001" } });

        var deltas = new List<string>();
        await foreach (var delta in chat.StreamAsync(new ChatStreamRequest
        {
            Question = "what is requisition from supplier SUP000001?",
            TopK = 5,
            MinSimilarity = 0,
        }))
        {
            deltas.Add(delta);
        }

        Assert.NotEmpty(deltas);
        Assert.All(deltas, d => Assert.False(string.IsNullOrWhiteSpace(d)));
        Assert.Equal(Answer, string.Concat(deltas));
    }

    /// <summary>
    /// A routed turn must produce the same report shape as an unrouted one.
    /// Routing is exactly the kind of change that quietly adds or drops a field,
    /// and the report is what the observability gate reads.
    /// </summary>
    [Fact]
    public async Task A_routed_turn_reports_the_same_fields_as_an_unrouted_one()
    {
        var unrouted = await ReportPropertiesForAsync(handoff: false);
        var routed = await ReportPropertiesForAsync(handoff: true);

        Assert.Equal(unrouted.OrderBy(p => p, StringComparer.Ordinal), routed.OrderBy(p => p, StringComparer.Ordinal));
    }

    /// <summary>
    /// The report must still name the tool the participant called. This is the
    /// assertion that would have caught the earlier misreading where a report
    /// looked as if it recorded no tool name at all.
    /// </summary>
    [Fact]
    public async Task A_routed_turn_records_the_participants_tool_call()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.AutoHandoff = true;
        chatClient.ToolCall = new FunctionCallContent(
            "call_1",
            ToolNames.SearchByCodes,
            new Dictionary<string, object?> { ["suppliers"] = new[] { "SUP000001" } });

        await chat.AnswerAsync(new ChatRequest
        {
            Question = "what is requisition from supplier SUP000001?",
            TopK = 5,
            MinSimilarity = 0,
        });

        var context = scope.ServiceProvider.GetRequiredService<AgentTurnContext>();
        var call = Assert.Single(context.ToolCalls);
        Assert.Equal(ToolNames.SearchByCodes, call.Name);
        Assert.NotNull(call.Arguments);
    }

    private static List<string> LastOffered(FakeChatClient chatClient)
    {
        var offered = chatClient.LastOfferedToolNames
            .Where(n => !n.StartsWith("handoff_to_", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(offered);
        return offered;
    }

    private async Task<List<string>> OfferedToolsForAsync(bool handoff)
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.AutoHandoff = handoff;

        if (handoff)
        {
            chatClient.ToolCall = new FunctionCallContent(
                "call_1",
                ToolNames.SearchByCodes,
                new Dictionary<string, object?> { ["suppliers"] = new[] { "SUP000001" } });
        }
        else
        {
            chatClient.ToolCall = new FunctionCallContent(
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
                });
        }

        await chat.AnswerAsync(new ChatRequest
        {
            Question = handoff ? "requisitions for supplier SUP000001?" : "create a requisition for ITM0001",
            TopK = 5,
            MinSimilarity = 0,
        });

        return LastOffered(chatClient);
    }

    private async Task<List<string>> ReportPropertiesForAsync(bool handoff)
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.AutoHandoff = true;
        chatClient.ToolCall = new FunctionCallContent(
            "call_1",
            handoff ? ToolNames.SearchByCodes : ToolNames.CreateRequisitionDraft,
            handoff
                ? new Dictionary<string, object?> { ["suppliers"] = new[] { "SUP000001" } }
                : new Dictionary<string, object?>
                {
                    ["supplierCode"] = "SUP000001",
                    ["item"] = "ITM0001",
                    ["description"] = "Pump",
                    ["quantity"] = 1,
                    ["date"] = "2026-01-15",
                    ["requester"] = "Jane Doe",
                });

        var response = await chat.AnswerAsync(new ChatRequest
        {
            Question = handoff ? "requisitions for supplier SUP000001?" : "create a requisition for ITM0001",
            TopK = 5,
            MinSimilarity = 0,
        });

        Assert.NotNull(response.Answer);

        var reportsDir = Path.Combine(_dataDir, "reports");
        var file = Directory.GetFiles(reportsDir, "*.json")
            .OrderByDescending(System.IO.File.GetLastWriteTimeUtc)
            .First();

        return JsonDocument.Parse(await System.IO.File.ReadAllTextAsync(file))
            .RootElement.EnumerateObject()
            .Select(p => p.Name)
            .ToList();
    }

    private async Task WriteJsonAsync(IEnumerable<PurchaseRequisitionImport> records)
    {
        var path = Path.Combine(_dataDir, "purchase.json");
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
    }
}
