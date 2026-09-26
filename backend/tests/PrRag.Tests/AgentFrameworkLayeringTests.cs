using Microsoft.Extensions.DependencyInjection;
using PrRag.Application.Domain;
using PrRag.Application.Services.Agents;
using PrRag.Application.Services.Agents.Specialists;
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

        var prompt = AgentInstructions.ComposeSystemPrompt(manifest, ActionBlocks());

        Assert.Contains(AgentInstructions.CoreInstructions, prompt);
        Assert.Contains("create-purchase-requisition: Guides drafting a new purchase requisition", prompt);
        Assert.Contains("reconcile-invoices: Guides matching invoices to requisitions", prompt);
    }

    [Fact]
    public async Task Agent_instructions_compose_prompt_with_empty_manifest()
    {
        var prompt = AgentInstructions.ComposeSystemPrompt(Array.Empty<SkillManifestEntry>(), ActionBlocks());

        Assert.Contains("No skills are available.", prompt);
    }

    /// <summary>
    /// An empty manifest means there is nothing to activate, so the prompt must
    /// not also tell the model to check the skill catalog before acting. The
    /// empty-manifest branch used to emit that rule unconditionally, so the
    /// prompt contradicted itself.
    /// </summary>
    [Fact]
    public async Task Empty_manifest_prompt_does_not_tell_the_model_to_consult_the_catalog()
    {
        var prompt = AgentInstructions.ComposeSystemPrompt(Array.Empty<SkillManifestEntry>(), ActionBlocks());

        Assert.DoesNotContain("check if there is a matching skill", prompt);
        Assert.DoesNotContain("Available skills guide recurring workflows.", prompt);
    }

    /// <summary>
    /// With skills present, the precedence rule and the guide must both be
    /// present — the fix above must not silence them.
    /// </summary>
    [Fact]
    public async Task Non_empty_manifest_prompt_keeps_the_skill_precedence_rule()
    {
        var manifest = new[]
        {
            new SkillManifestEntry("create-purchase-requisition", "Guides drafting a new purchase requisition"),
        };

        var prompt = AgentInstructions.ComposeSystemPrompt(manifest, ActionBlocks());

        Assert.Contains("check if there is a matching skill", prompt);
        Assert.Contains("Available skills guide recurring workflows.", prompt);
    }

    [Fact]
    public async Task Agent_name_is_the_orchestrator()
    {
        Assert.Equal("purchase-requisition-orchestrator", AgentInstructions.AgentName);
    }

    /// <summary>
    /// The capability units must partition the tool set exactly: every one of the
    /// seven wire names claimed by exactly one unit. An overlap would give the
    /// model two copies of a tool; a gap would silently drop one.
    /// </summary>
    [Fact]
    public async Task Capability_units_partition_the_seven_tool_names()
    {
        using var scope = _provider!.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<ISpecialistCatalog>();

        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            ToolNames.SearchByCodes,
            ToolNames.SearchSemantic,
            ToolNames.ActivateSkill,
            ToolNames.GetSuppliersByItem,
            ToolNames.CreateRequisitionDraft,
            ToolNames.ConfirmRequisitionDraft,
            ToolNames.CreateRequisition,
        };

        var perUnit = catalog.Specialists.ToDictionary(
            s => s.Id,
            s => s.Tools.Select(t => t.Name).ToList());

        var all = perUnit.Values.SelectMany(v => v).ToList();

        Assert.Equal(3, catalog.Specialists.Count);
        Assert.Equal(7, all.Count);
        Assert.Equal(expected, all.ToHashSet(StringComparer.Ordinal));

        // No wire name may be claimed twice, in any unit.
        foreach (var (id, names) in perUnit)
        {
            Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
            Assert.NotEmpty(names);
            Assert.All(names, n => Assert.Contains(n, expected));
            Assert.All(catalog.Specialists.Where(s => s.Id != id).SelectMany(s => s.Tools),
                t => Assert.DoesNotContain(t.Name, names));
        }

        // The partition must also hold at runtime, not just in the definitions.
        var runtimeNames = catalog.AllTools.Select(t => t.Name).ToList();
        Assert.Equal(7, runtimeNames.Count);
        Assert.Equal(expected, runtimeNames.ToHashSet(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Each_capability_unit_documents_exactly_the_tools_it_owns()
    {
        using var scope = _provider!.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<ISpecialistCatalog>();

        Assert.Equal(3, catalog.ActionBlocks.Count);
        Assert.Equal(
            catalog.Specialists.Select(s => s.ActionBlock),
            catalog.ActionBlocks);

        // Every tool the model can call must be named in some action block, or it
        // would reach the schema undocumented.
        var blocks = string.Join("\n", catalog.ActionBlocks);
        foreach (var tool in catalog.AllTools)
        {
            Assert.Contains(tool.Name, blocks);
        }
    }

    /// <summary>
    /// The real action blocks, in the real catalog order, so the prompt tests
    /// exercise the same text the agent actually ships.
    /// </summary>
    private static IReadOnlyList<string> ActionBlocks() =>
    [
        RequisitionSearchSpecialist.ActionBlock,
        RequisitionCreationSpecialist.ActionBlock,
        SkillActivationSpecialist.ActionBlock,
    ];

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