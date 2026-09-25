using Microsoft.Extensions.DependencyInjection;
using PrRag.Application.Domain;
using PrRag.Application.Services.Agents;
using Xunit;

namespace PrRag.Tests;

public class AgentFrameworkLayeringTests : IAsyncLifetime
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
    public async Task Agent_instructions_compose_prompt_with_skill_manifest()
    {
        var manifest = new[]
        {
            new SkillManifestEntry("create-purchase-requisition", "Guides drafting a new purchase requisition"),
            new SkillManifestEntry("reconcile-invoices", "Guides matching invoices to requisitions"),
        };

        var prompt = AgentInstructions.ComposeSystemPrompt(manifest);

        Assert.Contains(AgentInstructions.CoreInstructions, prompt);
        Assert.Contains("create-purchase-requisition: Guides drafting a new purchase requisition", prompt);
        Assert.Contains("reconcile-invoices: Guides matching invoices to requisitions", prompt);
    }

    [Fact]
    public async Task Agent_instructions_compose_prompt_with_empty_manifest()
    {
        var prompt = AgentInstructions.ComposeSystemPrompt(Array.Empty<SkillManifestEntry>());

        Assert.Contains("No skills are available.", prompt);
    }

    [Fact]
    public async Task Purchase_requisition_tools_expose_the_fixed_tools()
    {
        using var scope = _provider!.CreateScope();
        var tools = scope.ServiceProvider.GetRequiredService<PurchaseRequisitionTools>();

        var names = tools.All.Select(t => t.Name).ToHashSet();

        Assert.Equal(7, tools.All.Count);
        Assert.True(names.IsSupersetOf(new[]
        {
            PurchaseRequisitionTools.SearchByCodesTool,
            PurchaseRequisitionTools.SearchSemanticTool,
            PurchaseRequisitionTools.ActivateSkillTool,
            PurchaseRequisitionTools.GetSuppliersByItemTool,
            PurchaseRequisitionTools.CreateRequisitionDraftTool,
            PurchaseRequisitionTools.ConfirmRequisitionDraftTool,
            PurchaseRequisitionTools.CreateRequisitionTool,
        }));
    }

    [Fact]
    public async Task Skill_session_state_activates_injects_and_clears()
    {
        var session = await TestAgentSession.NewAsync();

        var before = SkillSessionState.DescribeForReport(session);
        Assert.False(before.Active);
        Assert.Null(before.SkillName);
        Assert.Null(SkillSessionState.ReadActiveSkillBody(session));

        SkillSessionState.Activate(session, "create-purchase-requisition", "purchasing assistant body");

        var active = SkillSessionState.DescribeForReport(session);
        Assert.True(active.Active);
        Assert.Equal("create-purchase-requisition", active.SkillName);
        Assert.Equal("purchasing assistant body", SkillSessionState.ReadActiveSkillBody(session));

        SkillSessionState.MarkInjected(session);
        Assert.Null(SkillSessionState.ReadActiveSkillBody(session));

        SkillSessionState.Clear(session);
        var cleared = SkillSessionState.DescribeForReport(session);
        Assert.False(cleared.Active);
        Assert.Null(cleared.SkillName);
    }

    [Fact]
    public async Task Requisition_draft_state_stages_presents_confirms_and_clears()
    {
        var session = await TestAgentSession.NewAsync();
        var draft = new RequisitionDraft("SUP000001", "ITM0001", "Hydraulic pump.", 3m, "2026-10-01", "Ana Souza");

        var empty = RequisitionDraftSessionState.Read(session);
        Assert.Null(empty.Draft);
        Assert.False(empty.Presented);
        Assert.False(empty.Confirmed);
        Assert.False(empty.HasConfirmedDraft);

        RequisitionDraftSessionState.Stage(session, draft);

        var staged = RequisitionDraftSessionState.Read(session);
        Assert.Equal(draft, staged.Draft);
        Assert.True(staged.Presented);
        Assert.False(staged.Confirmed);
        Assert.False(staged.HasConfirmedDraft);

        Assert.True(RequisitionDraftSessionState.MarkConfirmed(session));

        var confirmed = RequisitionDraftSessionState.Read(session);
        Assert.True(confirmed.Confirmed);
        Assert.True(confirmed.HasConfirmedDraft);
        Assert.Equal(draft, confirmed.Draft);

        RequisitionDraftSessionState.Clear(session);
        var clearedDraft = RequisitionDraftSessionState.Read(session);
        Assert.Null(clearedDraft.Draft);
        Assert.False(clearedDraft.Presented);
        Assert.False(clearedDraft.Confirmed);
    }

    [Fact]
    public async Task Re_drafting_clears_the_previous_confirmation()
    {
        var session = await TestAgentSession.NewAsync();
        var first = new RequisitionDraft("SUP000001", "ITM0001", "Hydraulic pump.", 3m, "2026-10-01", "Ana Souza");
        var revised = first with { Quantity = 5m };

        RequisitionDraftSessionState.Stage(session, first);
        Assert.True(RequisitionDraftSessionState.MarkConfirmed(session));

        // Editing a field after confirmation must require a fresh confirmation.
        RequisitionDraftSessionState.Stage(session, revised);

        var snapshot = RequisitionDraftSessionState.Read(session);
        Assert.Equal(5m, snapshot.Draft!.Quantity);
        Assert.True(snapshot.Presented);
        Assert.False(snapshot.Confirmed);
        Assert.False(snapshot.HasConfirmedDraft);
    }

    [Fact]
    public async Task Confirming_without_a_staged_draft_is_refused()
    {
        var session = await TestAgentSession.NewAsync();

        Assert.False(RequisitionDraftSessionState.MarkConfirmed(session));

        var snapshot = RequisitionDraftSessionState.Read(session);
        Assert.Null(snapshot.Draft);
        Assert.False(snapshot.Confirmed);
    }

    [Fact]
    public async Task Requisition_draft_state_reports_progress_flags()
    {
        var session = await TestAgentSession.NewAsync();

        var empty = RequisitionDraftSessionState.Read(session);
        Assert.Null(empty.Draft);
        Assert.Equal((false, false, false), (empty.Draft is not null, empty.Presented, empty.Confirmed));

        RequisitionDraftSessionState.Stage(
            session,
            new RequisitionDraft("SUP000001", "ITM0001", "Hydraulic pump.", 3m, "2026-10-01", "Ana Souza"));

        var staged = RequisitionDraftSessionState.Read(session);
        Assert.Equal((true, true, false), (staged.Draft is not null, staged.Presented, staged.Confirmed));

        RequisitionDraftSessionState.MarkConfirmed(session);
        var confirmed = RequisitionDraftSessionState.Read(session);
        Assert.Equal((true, true, true), (confirmed.Draft is not null, confirmed.Presented, confirmed.Confirmed));
    }
}