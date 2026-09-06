using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using PrRag.Application.Abstractions;
using PrRag.Application.DTOs;
using Xunit;

namespace PrRag.Tests;

public class SkillFrameworkTests : IAsyncLifetime
{
    private static string ConnectionTemplate => TestDatabase.ConnectionStringTemplate;

    private readonly string _dbName = $"prrag_test_{Guid.NewGuid():N}";
    private string _connectionString = null!;
    private ServiceProvider? _provider;
    private string _dataDir = null!;

    private const string SkillFile = """
        ---
        name: create-purchase-requisition
        description: Use this skill ONLY when the user wants to CREATE a new purchase requisition or draft a new one.
        version: 1
        ---
        # Role
        When this skill is active you act as a purchasing assistant that helps the user draft and persist a NEW purchase requisition.
        # Procedure
        1. Collect the required fields one at a time: supplier code, item code, description, quantity, delivery date (yyyy-MM-dd), requester.
        2. Validate item (ITM-*) and supplier (SUP*) codes with search_by_codes and flag any code with no match.
        3. Present a structured draft and ask for explicit confirmation.
        4. After the user confirms, call create_requisition with exactly the six validated fields and report the created file.
        """;

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
        };
        await WriteJsonAsync(records);

        var skillsDir = Path.Combine(_dataDir, "skills");
        Directory.CreateDirectory(skillsDir);
        await File.WriteAllTextAsync(Path.Combine(skillsDir, "create-purchase-requisition.md"), SkillFile);

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

    private static async Task<RagQueryReport> ReadLastReportAsync(string reportsDir)
    {
        var file = Directory.GetFiles(reportsDir, "*.json")
            .OrderByDescending(f => f)
            .First();
        var json = await File.ReadAllTextAsync(file);
        return JsonSerializer.Deserialize<RagQueryReport>(json)!;
    }

    private static string LastToolResult(FakeChatClient chatClient)
    {
        var toolMessage = chatClient.LastMessages.Last(m => m.Role == ChatRole.Tool);
        var content = toolMessage.Contents.OfType<FunctionResultContent>().Last();
        return content.Result?.ToString() ?? string.Empty;
    }

    private static FunctionCallContent ActivationCall(string skillName) =>
        new("call_act", "activate_skill", new Dictionary<string, object?> { ["name"] = skillName });

    private static FunctionCallContent CodesCall(IEnumerable<string> suppliers) =>
        new("call_codes", "search_by_codes", new Dictionary<string, object?> { ["suppliers"] = suppliers.ToArray() });

    [Fact]
    public async Task Skill_guidance_is_followed_with_code_validation_via_search_tool()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.ScriptedToolCalls.Add(ActivationCall("create-purchase-requisition"));
        chatClient.ScriptedToolCalls.Add(CodesCall(new[] { "SUP000001" }));

        var response = await chat.AnswerAsync(new ChatRequest
        {
            Question = "create a purchase requisition for supplier SUP000001",
            TopK = 5,
            MinSimilarity = 0,
        });

        Assert.Equal(1, response.RetrievedCount);
        Assert.NotEmpty(response.Answer);
        Assert.Contains("create-purchase-requisition", chatClient.LastPrompt);
    }

    [Fact]
    public async Task Unknown_skill_returns_error_and_conversation_continues()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.ScriptedToolCalls.Add(ActivationCall("does-not-exist"));

        var response = await chat.AnswerAsync(new ChatRequest
        {
            Question = "activate something",
            TopK = 5,
            MinSimilarity = 0,
        });

        Assert.NotEmpty(response.Answer);
        Assert.Contains("Unknown skill", LastToolResult(chatClient));
        Assert.Contains("create-purchase-requisition", chatClient.LastPrompt);
    }

    [Fact]
    public async Task Active_skill_guidance_is_restored_on_a_follow_up_turn()
    {
        var sessionId = Guid.NewGuid().ToString("N");
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.ScriptedToolCalls.Add(ActivationCall("create-purchase-requisition"));

        // Turn 1 activates the skill and persists it in the session state bag.
        var first = await chat.AnswerAsync(new ChatRequest
        {
            SessionId = sessionId,
            Question = "I want to create a purchase requisition.",
            TopK = 5,
            MinSimilarity = 0,
        });
        Assert.NotEmpty(first.Answer);

        // Turn 2 on the same session restores the skill guidance from session
        // state — no "[Skill: ...]" marker in the client history.
        var streamed = new List<string>();
        await foreach (var token in chat.StreamAsync(new ChatStreamRequest
        {
            SessionId = sessionId,
            Question = "the item is ITM0001, quantity 3, delivery 2026-10-01",
            TopK = 5,
            MinSimilarity = 0,
        }))
        {
            streamed.Add(token);
        }

        Assert.NotEmpty(streamed);
        Assert.Contains("purchasing assistant", chatClient.LastPrompt);
    }

    [Fact]
    public async Task Confirmed_requisition_is_persisted_via_create_requisition()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.ScriptedToolCalls.Add(ActivationCall("create-purchase-requisition"));
        chatClient.ScriptedToolCalls.Add(CodesCall(new[] { "SUP000001" }));
        chatClient.ScriptedToolCalls.Add(new FunctionCallContent(
            "call_create",
            "create_requisition",
            new Dictionary<string, object?>
            {
                ["supplierCode"] = "SUP000001",
                ["item"] = "ITM0001",
                ["description"] = "Hydraulic pump for maintenance.",
                ["quantity"] = 3m,
                ["date"] = "2026-10-01",
                ["requester"] = "Ana Souza",
            }));

        var response = await chat.AnswerAsync(new ChatRequest
        {
            Question = "create a purchase requisition: item ITM0001, qty 3, Acme (SUP000001), delivery 2026-10-01, requester Ana Souza",
            TopK = 5,
            MinSimilarity = 0,
        });

        Assert.NotEmpty(response.Answer);
        Assert.Contains("Requisition created", LastToolResult(chatClient));

        var file = Assert.Single(Directory.GetFiles(RequisitionsDir, "*.json"));
        var json = await File.ReadAllTextAsync(file);
        var written = JsonSerializer.Deserialize<NewPurchaseRequisition>(json)!;
        Assert.Equal("SUP000001", written.SupplierCode);
        Assert.Equal("ITM0001", written.Item);
        Assert.Equal("Hydraulic pump for maintenance.", written.Description);
        Assert.Equal(3m, written.Quantity);
        Assert.Equal("2026-10-01", written.Date);
        Assert.Equal("Ana Souza", written.Requester);
    }

    [Fact]
    public async Task Create_requisition_with_invalid_fields_writes_no_file()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.ScriptedToolCalls.Add(new FunctionCallContent(
            "call_create",
            "create_requisition",
            new Dictionary<string, object?>
            {
                ["supplierCode"] = string.Empty,
                ["item"] = "ITM0001",
                ["description"] = "Hydraulic pump for maintenance.",
                ["quantity"] = 3m,
                ["date"] = "not-a-date",
                ["requester"] = "Ana Souza",
            }));

        var response = await chat.AnswerAsync(new ChatRequest
        {
            Question = "persist a requisition",
            TopK = 5,
            MinSimilarity = 0,
        });

        Assert.NotEmpty(response.Answer);
        Assert.Contains("Missing or invalid required fields", LastToolResult(chatClient));
        Assert.False(Directory.Exists(RequisitionsDir) && Directory.GetFiles(RequisitionsDir, "*.json").Length > 0);
    }

    [Fact]
    public async Task Plain_question_answers_free_form_with_skill_off_in_report()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();
        chatClient.ToolCall = null;

        var response = await chat.AnswerAsync(new ChatRequest
        {
            Question = "hello there",
            TopK = 5,
            MinSimilarity = 0,
        });

        Assert.NotEmpty(response.Answer);
        Assert.Equal(0, response.RetrievedCount);

        var report = await ReadLastReportAsync(Path.Combine(_dataDir, "reports"));
        Assert.False(report.SkillActivated);
        Assert.Null(report.SkillId);
        Assert.Null(report.SkillName);
    }

    [Fact]
    public async Task Skill_guided_request_records_skill_in_report()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.ScriptedToolCalls.Add(ActivationCall("create-purchase-requisition"));

        await chat.AnswerAsync(new ChatRequest
        {
            Question = "create a purchase requisition",
            TopK = 5,
            MinSimilarity = 0,
        });

        var report = await ReadLastReportAsync(Path.Combine(_dataDir, "reports"));
        Assert.True(report.SkillActivated);
        Assert.Equal("create-purchase-requisition", report.SkillId);
        Assert.Equal("create-purchase-requisition", report.SkillName);
    }

    private async Task WriteJsonAsync(IEnumerable<PurchaseRequisitionImport> records)
    {
        var path = Path.Combine(_dataDir, "purchase.json");
        var json = JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
    }
}