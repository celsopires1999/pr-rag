using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using PrRag.Application.Abstractions;
using PrRag.Application.DTOs;
using PrRag.Application.Services.Agents;
using PrRag.Application.Services.Agents.Specialists;
using PrRag.Infrastructure.Persistence;
using Xunit;

namespace PrRag.Tests;

/// <summary>
/// Guards the tool contract the chat model actually sees. The parameter
/// descriptions only reach the wire if each tool is registered from its
/// <c>[Description]</c>-annotated method rather than a forwarding lambda —
/// nothing else in the suite would notice that regression.
/// </summary>
public class ToolSchemaTests : IAsyncLifetime
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
                ItemName = "Hydraulic Oil",
                Description = "Procurement of Hydraulic Oil for maintenance operations.",
            },
            new PurchaseRequisitionImport
            {
                PurchaseRequisition = "PR00000002",
                SupplierCode = "SUP000002",
                SupplierName = "Beta Components Ltd",
                Item = "ITM0001",
                ItemName = "Hydraulic Oil",
                Description = "Procurement of Hydraulic Oil for maintenance operations.",
            },
        };
        var path = Path.Combine(_dataDir, "purchase.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(records));

        using var scope = _provider!.CreateScope();
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

    [Fact]
    public void Every_tool_exposes_a_description()
    {
        foreach (var function in Functions())
        {
            Assert.False(string.IsNullOrWhiteSpace(function.Description), $"Tool '{function.Name}' has no description.");
        }
    }

    [Fact]
    public void Every_tool_parameter_has_a_description()
    {
        foreach (var function in Functions())
        {
            var schema = SchemaOf(function);
            Assert.Equal(JsonValueKind.Object, schema.ValueKind);

            foreach (var property in schema.GetProperty("properties").EnumerateObject())
            {
                if (!property.Value.TryGetProperty("description", out var description))
                {
                    Assert.Fail($"Tool '{function.Name}' parameter '{property.Name}' has no description in its schema.");
                }

                Assert.False(
                    string.IsNullOrWhiteSpace(description.GetString()),
                    $"Tool '{function.Name}' parameter '{property.Name}' has a blank description.");
            }
        }
    }

    [Fact]
    public void Optional_code_search_parameters_stay_optional()
    {
        var schema = SchemaOf(FunctionNamed(ToolNames.SearchByCodes));

        var required = schema.TryGetProperty("required", out var requiredProperty)
            ? requiredProperty.EnumerateArray().Select(e => e.GetString()).ToArray()
            : [];

        Assert.Empty(required);
    }

    [Fact]
    public async Task Tool_result_reaches_the_model_as_serialized_json()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.ScriptedToolCalls.Add(new FunctionCallContent(
            "call_suppliers",
            ToolNames.GetSuppliersByItem,
            new Dictionary<string, object?> { ["item"] = "ITM0001" }));

        await chat.AnswerAsync(new ChatRequest
        {
            Question = "Which suppliers provided the item Hydraulic Oil?",
            TopK = 5,
            MinSimilarity = 0,
        });

        var toolMessage = chatClient.LastMessages.Last(m => m.Role == ChatRole.Tool);
        Assert.Contains(toolMessage.Contents, c => c is FunctionResultContent);

        // Whatever concrete type marshalling produces, the model must receive
        // serialized JSON — never a .NET type name from a raw passthrough.
        var json = chatClient.LastToolResultJson();

        Assert.Equal("Acme Industrial Supply", SupplierNamed(json, "SUP000001"));
        Assert.Equal("Beta Components Ltd", SupplierNamed(json, "SUP000002"));
        Assert.Equal(2, ReadCount(json));
        Assert.DoesNotContain("PrRag.Application", json);
    }

    [Fact]
    public void The_creation_flow_tools_are_present_and_described()
    {
        foreach (var name in new[]
        {
            ToolNames.CreateRequisitionDraft,
            ToolNames.ConfirmRequisitionDraft,
            ToolNames.CreateRequisition,
        })
        {
            var function = FunctionNamed(name);

            Assert.False(string.IsNullOrWhiteSpace(function.Description), $"Tool '{name}' has no description.");

            foreach (var property in SchemaOf(function).GetProperty("properties").EnumerateObject())
            {
                Assert.True(
                    property.Value.TryGetProperty("description", out var description)
                        && !string.IsNullOrWhiteSpace(description.GetString()),
                    $"Tool '{name}' parameter '{property.Name}' has no description in its schema.");
            }
        }
    }

    /// <summary>
    /// The whole point of the gate: a model that calls create_requisition
    /// without ever drafting and confirming must write nothing. This is the test
    /// that would fail if the gate were ever moved back into prompt text.
    /// </summary>
    [Fact]
    public async Task Create_requisition_without_a_confirmed_draft_persists_nothing()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        // Six valid fields for a registered combination, but no draft and no
        // confirmation — the exact shape the model used before this change.
        chatClient.ScriptedToolCalls.Add(RequisitionFlow.Create());

        var response = await chat.AnswerAsync(new ChatRequest
        {
            Question = "create a purchase requisition",
            TopK = 5,
            MinSimilarity = 0,
        });

        Assert.NotEmpty(response.Answer);

        var result = chatClient.LastToolResultJson();
        Assert.Contains("\"success\":false", result.Replace(" ", ""));
        Assert.Contains(ToolNames.CreateRequisitionDraft, result);
        Assert.Contains(ToolNames.ConfirmRequisitionDraft, result);

        using var verify = _provider!.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<PrRagDbContext>();
        Assert.Empty(await db.CreatedRequisitions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Create_requisition_with_a_staged_but_unconfirmed_draft_persists_nothing()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        // Drafted but the user never said yes.
        chatClient.ScriptedToolCalls.Add(RequisitionFlow.Draft());
        chatClient.ScriptedToolCalls.Add(RequisitionFlow.Create());

        await chat.AnswerAsync(new ChatRequest
        {
            Question = "create a purchase requisition",
            TopK = 5,
            MinSimilarity = 0,
        });

        var result = chatClient.LastToolResultJson();
        Assert.Contains("\"success\":false", result.Replace(" ", ""));
        Assert.Contains(ToolNames.ConfirmRequisitionDraft, result);

        using var verify = _provider!.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<PrRagDbContext>();
        Assert.Empty(await db.CreatedRequisitions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task A_declined_draft_does_not_unlock_creation()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.ScriptedToolCalls.Add(RequisitionFlow.Draft());
        chatClient.ScriptedToolCalls.Add(RequisitionFlow.Confirm("no, the quantity is wrong"));
        chatClient.ScriptedToolCalls.Add(RequisitionFlow.Create());

        await chat.AnswerAsync(new ChatRequest
        {
            Question = "create a purchase requisition",
            TopK = 5,
            MinSimilarity = 0,
        });

        var result = chatClient.LastToolResultJson();
        Assert.Contains("\"success\":false", result.Replace(" ", ""));

        using var verify = _provider!.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<PrRagDbContext>();
        Assert.Empty(await db.CreatedRequisitions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Arguments_contradicting_the_confirmed_draft_are_refused()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        RequisitionFlow.ScriptConfirmedDraft(chatClient.ScriptedToolCalls);

        // The model drafts quantity 3, then tries to persist quantity 99.
        chatClient.ScriptedToolCalls.Add(RequisitionFlow.Create(quantity: 99m));

        await chat.AnswerAsync(new ChatRequest
        {
            Question = "create a purchase requisition",
            TopK = 5,
            MinSimilarity = 0,
        });

        var result = chatClient.LastToolResultJson();
        Assert.Contains("\"success\":false", result.Replace(" ", ""));
        Assert.Contains("quantity", result);

        using var verify = _provider!.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<PrRagDbContext>();
        Assert.Empty(await db.CreatedRequisitions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task A_second_creation_in_the_same_session_is_refused()
    {
        var sessionId = Guid.NewGuid().ToString("N");
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        RequisitionFlow.ScriptConfirmedCreation(
            chatClient.ScriptedToolCalls,
            callIdSuffix: "first");

        await chat.AnswerAsync(new ChatRequest
        {
            SessionId = sessionId,
            Question = "create a purchase requisition",
            TopK = 5,
            MinSimilarity = 0,
        });

        using var verify = _provider!.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<PrRagDbContext>();
        Assert.Single(await db.CreatedRequisitions.AsNoTracking().ToListAsync());

        // The draft was cleared on success, so a second call has nothing to
        // confirm and must be refused.
        chatClient.ResetScript();
        chatClient.ScriptedToolCalls.Add(RequisitionFlow.Create());

        await chat.AnswerAsync(new ChatRequest
        {
            SessionId = sessionId,
            Question = "create another one",
            TopK = 5,
            MinSimilarity = 0,
        });

        Assert.Contains("\"success\":false", chatClient.LastToolResultJson().Replace(" ", ""));

        using var verifyAgain = _provider!.CreateScope();
        var dbAgain = verifyAgain.ServiceProvider.GetRequiredService<PrRagDbContext>();
        Assert.Single(await dbAgain.CreatedRequisitions.AsNoTracking().ToListAsync());
    }

    /// <summary>
    /// Staging a draft is session state only: nothing reaches Postgres, so an
    /// abandoned conversation leaves no trace in the created-requisitions table.
    /// </summary>
    [Fact]
    public async Task Staging_a_draft_never_reaches_the_database()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.ScriptedToolCalls.Add(RequisitionFlow.Draft());

        await chat.AnswerAsync(new ChatRequest
        {
            Question = "start a requisition",
            TopK = 5,
            MinSimilarity = 0,
        });

        Assert.Contains("\"staged\":true", chatClient.LastToolResultJson().Replace(" ", ""));

        using var verify = _provider!.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<PrRagDbContext>();
        Assert.Empty(await db.CreatedRequisitions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Report_distinguishes_a_confirmed_creation_from_a_refusal()
    {
        var reportsDir = Path.Combine(_dataDir, "reports");

        using (var scope = _provider!.CreateScope())
        {
            var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
            var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

            RequisitionFlow.ScriptConfirmedCreation(chatClient.ScriptedToolCalls);

            await chat.AnswerAsync(new ChatRequest
            {
                Question = "create a purchase requisition",
                TopK = 5,
                MinSimilarity = 0,
            });
        }

        var confirmed = await ReadLastReportAsync(reportsDir);
        Assert.True(confirmed.RequisitionDraftStaged);
        Assert.True(confirmed.RequisitionDraftPresented);
        Assert.True(confirmed.RequisitionDraftConfirmed);
        Assert.True(confirmed.RequisitionPersisted);

        using (var scope = _provider!.CreateScope())
        {
            var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
            var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

            chatClient.ScriptedToolCalls.Add(RequisitionFlow.Create());

            await chat.AnswerAsync(new ChatRequest
            {
                Question = "create a purchase requisition",
                TopK = 5,
                MinSimilarity = 0,
            });
        }

        var refused = await ReadLastReportAsync(reportsDir);
        Assert.False(refused.RequisitionDraftConfirmed);
        Assert.False(refused.RequisitionPersisted);
    }

    [Fact]
    public async Task A_turn_without_requisition_activity_leaves_the_new_report_fields_empty()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();
        chatClient.ToolCall = null;

        await chat.AnswerAsync(new ChatRequest
        {
            Question = "hello there",
            TopK = 5,
            MinSimilarity = 0,
        });

        var report = await ReadLastReportAsync(Path.Combine(_dataDir, "reports"));
        Assert.False(report.RequisitionDraftStaged);
        Assert.False(report.RequisitionDraftPresented);
        Assert.False(report.RequisitionDraftConfirmed);
        Assert.False(report.RequisitionPersisted);

        // Pre-existing fields keep their behavior.
        Assert.Equal(0, report.RetrievedCount);
        Assert.True(report.UsedNoContextFallback);
        Assert.False(report.SkillActivated);
        Assert.Empty(report.ToolCalls);
    }

    private static async Task<RagQueryReport> ReadLastReportAsync(string reportsDir)
    {
        var file = Directory.GetFiles(reportsDir, "*.json").OrderByDescending(f => f).First();
        return JsonSerializer.Deserialize<RagQueryReport>(await File.ReadAllTextAsync(file))!;
    }

    private static string? SupplierNamed(string json, string supplierCode)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement
            .GetProperty("suppliers")
            .EnumerateArray()
            .Where(s => s.GetProperty("supplierCode").GetString() == supplierCode)
            .Select(s => s.GetProperty("supplierName").GetString())
            .SingleOrDefault();
    }

    private static int ReadCount(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("count").GetInt32();
    }

    private IEnumerable<AIFunction> Functions()
    {
        using var scope = _provider!.CreateScope();
        return scope.ServiceProvider
            .GetRequiredService<ISpecialistCatalog>()
            .AllTools
            .OfType<AIFunction>()
            .ToList();
    }

    private AIFunction FunctionNamed(string name) =>
        Functions().Single(f => f.Name == name);

    private static JsonElement SchemaOf(AIFunction function)
    {
        JsonElement schema = function.JsonSchema;

        Assert.NotEqual(JsonValueKind.Undefined, schema.ValueKind);
        return schema;
    }
}
