using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using PrRag.Application.Abstractions;
using PrRag.Application.Domain;
using PrRag.Application.DTOs;
using PrRag.Infrastructure.Persistence;
using Xunit;
using PrRag.Application.Services.Agents;
using PrRag.Application.Services.Agents.Specialists;

namespace PrRag.Tests;

public class SkillFrameworkTests : IAsyncLifetime
{
    private static string ConnectionTemplate => TestDatabase.ConnectionStringTemplate;

    private readonly string _dbName = $"prrag_test_{Guid.NewGuid():N}";
    private string _connectionString = null!;
    private ServiceProvider? _provider;
    private FakeEmbeddingService? _embeddings;
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
        2. You cannot verify codes yourself: the lookup tools belong to another agent. Never tell the user a code has been verified.
        3. create_requisition performs the authoritative check and refuses an item and supplier combination that has no existing requisition, writing nothing when it does.
        4. Present a structured draft and ask for explicit confirmation.
        5. After the user confirms, call create_requisition with exactly the six validated fields and report the created requisition id.
        """;

    public async Task InitializeAsync()
    {
        _connectionString = $"{ConnectionTemplate};Database={_dbName}";
        (_provider, _embeddings, _dataDir) = IntegrationServiceFactory.Create(_connectionString);
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

    private static async Task<List<CreatedRequisition>> CreatedRequisitionsAsync(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PrRagDbContext>();
        return await db.CreatedRequisitions.AsNoTracking().ToListAsync();
    }

    private static async Task<int> HistoricalRequisitionCountAsync(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PrRagDbContext>();
        return await db.PurchaseRequisitions.AsNoTracking().CountAsync();
    }

    private static async Task<RagQueryReport> ReadLastReportAsync(string reportsDir)
    {
        var file = Directory.GetFiles(reportsDir, "*.json")
            .OrderByDescending(f => f)
            .First();
        var json = await File.ReadAllTextAsync(file);
        return JsonSerializer.Deserialize<RagQueryReport>(json)!;
    }

    private static string LastToolResult(FakeChatClient chatClient) =>
        chatClient.LastToolResultJson();

    private static string ExtractRequisitionId(string toolResult)
    {
        using var document = JsonDocument.Parse(toolResult);
        var result = document.RootElement;

        Assert.True(
            result.GetProperty("success").GetBoolean(),
            $"Expected a successful create_requisition result: {toolResult}");

        return result.GetProperty("requisitionId").GetString()!;
    }

    private static FunctionCallContent ActivationCall(string skillName) =>
        new("call_act", "activate_skill", new Dictionary<string, object?> { ["name"] = skillName });

    private static FunctionCallContent CodesCall(IEnumerable<string> suppliers) =>
        new("call_codes", "search_by_codes", new Dictionary<string, object?> { ["suppliers"] = suppliers.ToArray() });

    [Fact]
    public async Task An_active_skill_survives_a_turn_that_is_routed_to_a_participant()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        // Turn 1: the orchestrator activates the skill. The manifest is the
        // orchestrator's, so ask for the prompt by the tool that proves who held it.
        chatClient.AutoHandoff = false;
        chatClient.ScriptFor(ToolNames.ActivateSkill, ActivationCall("create-purchase-requisition"));

        await chat.AnswerAsync(new ChatRequest
        {
            Question = "create a purchase requisition for supplier SUP000001",
            TopK = 5,
            MinSimilarity = 0,
            SessionId = "skill-then-handoff",
        });

        Assert.Contains("create-purchase-requisition", chatClient.PromptForAgent(ToolNames.ActivateSkill));

        // Turn 2: a lookup question, so the turn is handed to the retrieval
        // specialist. The skill is still active, and its guidance reaches the
        // participant as a run-level system message even though the participant's
        // own instructions carry no manifest — only the orchestrator holds
        // activate_skill, so only the orchestrator can be told what exists.
        chatClient.HandOffToRetrieval(scope.ServiceProvider);
        chatClient.ScriptFor(ToolNames.SearchByCodes, CodesCall(new[] { "SUP000001" }));

        var response = await chat.AnswerAsync(new ChatRequest
        {
            Question = "what requisitions exist for supplier SUP000001?",
            TopK = 5,
            MinSimilarity = 0,
            SessionId = "skill-then-handoff",
        });

        Assert.Equal(1, response.RetrievedCount);
        Assert.NotEmpty(response.Answer);

        // The participant saw the active skill's guidance.
        Assert.Contains(
            chatClient.LastMessages,
            m => m.Role == ChatRole.System
                 && m.Text.Contains("purchasing assistant", StringComparison.Ordinal));
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

        // The orchestrator activates the skill, then hands off; the write tools
        // live on the creation agent, so the creation sequence is scripted there.
        //
        // The shipped skill also tells the agent to look the codes up first, and
        // that step now needs a handoff of its own — a turn reaches one specialist
        // and returns, so a lookup and a write are two turns. It is left out here
        // because it is not what this test is about; the lookup's own path is
        // covered by An_active_skill_survives_a_turn_that_is_routed_to_a_participant.
        chatClient.ScriptedToolCalls.Add(ActivationCall("create-purchase-requisition"));
        RequisitionFlow.ScriptConfirmedCreation(chatClient, scope.ServiceProvider);

        var response = await chat.AnswerAsync(new ChatRequest
        {
            Question = "create a purchase requisition: item ITM0001, qty 3, Acme (SUP000001), delivery 2026-10-01, requester Ana Souza",
            TopK = 5,
            MinSimilarity = 0,
        });

        Assert.NotEmpty(response.Answer);
        Assert.Contains("Requisition created", LastToolResult(chatClient));

        var requisitionId = ExtractRequisitionId(LastToolResult(chatClient));
        var created = await CreatedRequisitionsAsync(_provider!);
        var stored = Assert.Single(created, r => r.Id.ToString("N") == requisitionId);
        Assert.Equal("SUP000001", stored.SupplierCode);
        Assert.Equal("ITM0001", stored.Item);
        Assert.Equal("Hydraulic pump for maintenance.", stored.Description);
        Assert.Equal(3m, stored.Quantity);
        Assert.Equal("2026-10-01", stored.Date);
        Assert.Equal("Ana Souza", stored.Requester);
    }

    [Fact]
    public async Task Created_requisition_is_isolated_from_historical_dataset()
    {
        var historicalBefore = await HistoricalRequisitionCountAsync(_provider!);
        var embeddingsBefore = _embeddings!.CallCount;

        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.ScriptedToolCalls.Add(ActivationCall("create-purchase-requisition"));
        RequisitionFlow.ScriptConfirmedCreation(chatClient, scope.ServiceProvider);

        await chat.AnswerAsync(new ChatRequest
        {
            Question = "create a purchase requisition: item ITM0001, qty 3, Acme (SUP000001), delivery 2026-10-01, requester Ana Souza",
            TopK = 5,
            MinSimilarity = 0,
        });

        Assert.Single(await CreatedRequisitionsAsync(_provider!));

        // The historical dataset, its embeddings, and ingestion runtime are untouched.
        Assert.Equal(historicalBefore, await HistoricalRequisitionCountAsync(_provider!));
        Assert.Equal(embeddingsBefore, _embeddings!.CallCount);
    }

    [Fact]
    public async Task Draft_with_invalid_fields_persists_nothing()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        // Field validation happens when the draft is staged: an invalid draft
        // can never reach the confirmed state, so create_requisition cannot
        // later persist it.
        RequisitionFlow.OnCreationAgent(
            chatClient,
            scope.ServiceProvider,
            RequisitionFlow.Draft(supplierCode: string.Empty, date: "not-a-date"));

        var response = await chat.AnswerAsync(new ChatRequest
        {
            Question = "stage a requisition draft",
            TopK = 5,
            MinSimilarity = 0,
        });

        Assert.NotEmpty(response.Answer);
        Assert.Contains("Missing or invalid required fields", LastToolResult(chatClient));
        Assert.Contains("\"staged\":false", LastToolResult(chatClient).Replace(" ", ""));
        Assert.Empty(await CreatedRequisitionsAsync(_provider!));
    }

    [Fact]
    public async Task Confirmed_draft_with_unknown_item_supplier_combination_persists_nothing()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        // The user confirmed this draft, so the confirmation gate is satisfied.
        // The combination guard is independent of it and must still refuse.
        RequisitionFlow.ScriptConfirmedCreation(
            chatClient,
            scope.ServiceProvider,
            supplierCode: "SUP000002",
            item: "ITM9999");

        await chat.AnswerAsync(new ChatRequest
        {
            Question = "persist a requisition",
            TopK = 5,
            MinSimilarity = 0,
        });

        Assert.Contains("not registered for that item", LastToolResult(chatClient));
        Assert.Empty(await CreatedRequisitionsAsync(_provider!));
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

    /// <summary>
    /// The failure the live creation gate found on the <c>stage</c> probe: the
    /// orchestrator presented a draft it had never staged.
    ///
    /// <para>
    /// The guidance is delivered as a run-level system message, so it reaches every
    /// agent in the run — including the orchestrator, which is not the agent the
    /// procedure was written for. Step 4 of the fixture skill is the sharp end of
    /// that: "Present a structured draft and ask for explicit confirmation" names no
    /// tool, so an agent holding none can satisfy it in prose and nothing fails.
    ///
    /// <para>
    /// So this asserts the two channels are both present on the same turn, rather
    /// than that the guidance beat the instructions. The orchestrator's own rule —
    /// that a step whose tool it lacks is not its to perform — is delivered as
    /// <c>ChatOptions.Instructions</c>, the guidance as a message, and the defect
    /// was never one channel replacing the other. It is the model preferring the
    /// message, because the message is later and more specific. Asserting both
    /// channels carry their load is what a context-assembly regression would break,
    /// and it is the part of this that is deterministic.
    /// </summary>
    [Fact]
    public async Task Activated_guidance_does_not_displace_the_orchestrator_instructions()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        // Turn 1 activates, so turn 2 is the turn the guidance is injected on.
        chatClient.ScriptedToolCalls.Add(ActivationCall("create-purchase-requisition"));
        await chat.AnswerAsync(new ChatRequest
        {
            Question = "create a purchase requisition",
            TopK = 5,
            MinSimilarity = 0,
            SessionId = "guidance-does-not-displace",
        });

        await chat.AnswerAsync(new ChatRequest
        {
            Question = "supplier SUP000001, item ITM0001, quantity 5",
            TopK = 5,
            MinSimilarity = 0,
            SessionId = "guidance-does-not-displace",
        });

        // The guidance arrived, as a run-level system message.
        Assert.Contains(
            chatClient.LastMessages,
            m => m.Role == ChatRole.System
                 && m.Text.Contains("Present a structured draft", StringComparison.Ordinal));

        // And the orchestrator's own instructions are still on that same turn,
        // carrying the rule that step is not the orchestrator's to perform.
        var orchestratorPrompt = chatClient.PromptForAgent(ToolNames.ActivateSkill);
        Assert.Contains(
            "A step you cannot perform is not yours to carry out",
            orchestratorPrompt,
            StringComparison.Ordinal);
        Assert.Contains(
            "Never present an artifact that no tool returned",
            orchestratorPrompt,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The activation result says who the guidance it hands over is for, and says
    /// it nowhere else.
    /// </summary>
    /// <remarks>
    /// The capability rule already exists in the orchestrator's own instructions,
    /// and it lost: the guidance arrives later, as a run-level system message, and
    /// a model that has just read a procedure is inclined to perform it. So the
    /// statement is also emitted by the tool that hands the guidance over —
    /// adjacent to it, on that turn, in a channel a model weights differently from
    /// an instruction it was given earlier.
    /// <para>
    /// Asserted on the tool result rather than on the prompt because that is where
    /// it lives, and the second assertion is the half that keeps it safe: it names
    /// no destination and no specialist. The graph owns routing, skill text is
    /// policed for steering it, and this is a third channel that nothing policed —
    /// so a line here telling the model where to send a turn would be a rule with
    /// no guard and a new failure mode.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_activation_result_states_who_the_guidance_it_hands_over_is_for()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        chatClient.AutoHandoff = false;
        chatClient.ScriptedToolCalls.Add(ActivationCall("create-purchase-requisition"));

        await chat.AnswerAsync(new ChatRequest
        {
            Question = "create a purchase requisition",
            TopK = 5,
            MinSimilarity = 0,
            SessionId = "activation-states-ownership",
        });

        using var document = JsonDocument.Parse(LastToolResult(chatClient));
        var result = document.RootElement;
        var message = result.GetProperty("message").GetString()!;
        var body = result.GetProperty("body").GetString()!;

        // The statement is present, and it travels with the guidance rather than
        // replacing it: an agent that has just read a procedure is the one most
        // likely to perform it, so the qualification is what has to arrive next.
        Assert.Contains("the capability that owns the tools they name", message, StringComparison.Ordinal);
        Assert.Contains("without giving you those tools", message, StringComparison.Ordinal);
        Assert.Contains(body, message, StringComparison.Ordinal);

        // The guidance itself is untouched, because the body is what later turns
        // are given. A statement folded into it would be restated on every turn
        // thereafter, which is how a run never ends.
        Assert.DoesNotContain("Guidance, not capability", body, StringComparison.Ordinal);

        // No routing, in this channel or any other.
        foreach (var phrase in new[]
                 {
                     AgentIds.Orchestrator, AgentIds.Creation, AgentIds.Retrieval,
                     "specialist", "hand off", "route to", "delegate to",
                 })
        {
            Assert.False(
                SkillActivationSpecialist.OwnershipNotice.Contains(phrase, StringComparison.OrdinalIgnoreCase),
                $"The activation result's ownership statement names '{phrase}'. The graph owns routing, and " +
                "a tool result that tells the model where to send a turn is a rule in a channel nothing polices.");
        }
    }

    private async Task WriteJsonAsync(IEnumerable<PurchaseRequisitionImport> records)
    {
        var path = Path.Combine(_dataDir, "purchase.json");
        var json = JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
    }
}