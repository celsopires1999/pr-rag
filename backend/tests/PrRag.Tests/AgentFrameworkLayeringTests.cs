using System.Reflection;
using System.Text.RegularExpressions;
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
    public void Skill_session_state_activates_injects_and_clears()
    {
        var session = new AgentSessionState();

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
    public void Requisition_draft_state_stages_presents_confirms_and_clears()
    {
        var session = new AgentSessionState();
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
        var session = new AgentSessionState();
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
        var session = new AgentSessionState();

        Assert.False(RequisitionDraftSessionState.MarkConfirmed(session));

        var snapshot = RequisitionDraftSessionState.Read(session);
        Assert.Null(snapshot.Draft);
        Assert.False(snapshot.Confirmed);
    }

    [Fact]
    public void Requisition_draft_state_reports_progress_flags()
    {
        var session = new AgentSessionState();

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
    /// <summary>
    /// The partition restated per agent rather than per capability unit.
    /// </summary>
    /// <remarks>
    /// A unit-level partition is the weaker claim: two units can each hold a
    /// clean, disjoint set of tools and still land on the same agent, which
    /// would hand that agent a capability it was never told about. Grouping by
    /// the owning agent is the shape the model actually sees, so that is what is
    /// asserted — every wire name on exactly one agent, and the orchestrator
    /// holding nothing that belongs to the participant.
    /// </remarks>
    [Fact]
    public void Each_tool_is_assigned_to_exactly_one_agent()
    {
        using var scope = _provider!.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<ISpecialistCatalog>();

        // A unit with no slug has not been extracted: its tools stay on the
        // orchestrator, which is what makes it the orchestrator's.
        var byAgent = catalog.Specialists
            .GroupBy(s => s.AgentSlug ?? AgentIds.Orchestrator, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.SelectMany(s => s.Tools).Select(t => t.Name).ToList(),
                StringComparer.Ordinal);

        Assert.Equal(3, byAgent.Count);

        var retrieval = byAgent[AgentIds.Retrieval];
        var creation = byAgent[AgentIds.Creation];

        // The read capability owns the three read tools and nothing else, and the
        // write capability the three write tools and nothing else. Asserting both
        // against ToolNames' read and write sets is what keeps the two from
        // quietly converging on a shared tool.
        Assert.Equal(
            ToolNames.ReadOnly.OrderBy(n => n, StringComparer.Ordinal),
            retrieval.OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(
            ToolNames.Writes.OrderBy(n => n, StringComparer.Ordinal),
            creation.OrderBy(n => n, StringComparer.Ordinal));

        // The orchestrator keeps only what no specialist claims, and that leftover
        // is written out as a set rather than derived as "whatever is left". A
        // derived leftover keeps passing while a tool is handed to the
        // orchestrator, which is the mistake this change exists to prevent: the
        // write path is unreachable while the agent that fronts the conversation
        // still holds the tools it can no longer be given.
        var unextracted = catalog.Specialists
            .Where(s => !s.IsExtracted)
            .SelectMany(s => s.Tools.Select(t => t.Name))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            new[] { ToolNames.ActivateSkill }.OrderBy(n => n, StringComparer.Ordinal),
            unextracted);

        var orchestrator = byAgent[AgentIds.Orchestrator];
        Assert.Equal(unextracted, orchestrator.OrderBy(n => n, StringComparer.Ordinal));

        // The front door holds no write tool, and neither capability holds the
        // other's kind. Stated directly so a future tool cannot land on the wrong
        // agent without one of these four failing.
        Assert.Empty(ToolNames.Writes.Intersect(orchestrator, StringComparer.Ordinal));
        Assert.Empty(ToolNames.Writes.Intersect(retrieval, StringComparer.Ordinal));
        Assert.Empty(ToolNames.ReadOnly.Intersect(creation, StringComparer.Ordinal));
        Assert.Empty(ToolNames.ReadOnly.Intersect(orchestrator, StringComparer.Ordinal));

        // Exactly the seven wire names, once each, across the three agents.
        var union = byAgent.Values.SelectMany(v => v).ToList();
        Assert.Equal(7, union.Count);
        Assert.Equal(7, union.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// A unit's action block is prompt input, so a wire name it does not own is an
    /// instruction the agent cannot follow.
    /// </summary>
    /// <remarks>
    /// This is the shipped-skill guard applied to the other prompt text that
    /// ships. The create skill once told the orchestrator to validate codes with
    /// <c>search_by_codes</c>; after the read tools moved to their own agent the
    /// step silently never ran and no test failed, because nothing checked prompt
    /// text against the tools an agent is offered. Asserting the absence per unit
    /// is the check that was missing.
    /// </remarks>
    [Fact]
    public void No_units_action_block_names_a_tool_it_does_not_own()
    {
        using var scope = _provider!.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<ISpecialistCatalog>();

        foreach (var unit in catalog.Specialists)
        {
            var owned = unit.Tools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
            var foreign = catalog.AllTools
                .Select(t => t.Name)
                .Where(n => !owned.Contains(n))
                .Where(n => unit.ActionBlock.Contains(n, StringComparison.Ordinal))
                .ToList();

            Assert.True(
                foreign.Count == 0,
                $"{unit.Id}'s action block names {string.Join(", ", foreign)}, which it is not offered.");
        }
    }

    /// <summary>
    /// The shipped create skill must name calls inside a single capability.
    /// </summary>
    /// <remarks>
    /// This is the shipped-skill bug turned around, and it is why the rule belongs
    /// next to the catalog rather than in a text fixture. The skill tells its
    /// reader to call the three write tools. Before the extraction the reader was
    /// the orchestrator and that was correct; now the reader is still the
    /// orchestrator, since only the orchestrator can activate a skill, but the
    /// orchestrator owns none of them. The flow still works, because the activated
    /// guidance is injected at run level and so reaches the creation agent too — but
    /// nothing in the text says that, and the orchestrator's own handoff rule is what
    /// keeps the turn moving.
    /// <para>
    /// A single-owner rule catches both directions of this. It fails if a skill
    /// straddles two units, which is the state the retrieval split left the create
    /// skill in when it still told the orchestrator to validate codes with
    /// <c>search_by_codes</c> — the model would have to hand off mid-skill with
    /// nothing in the skill telling it to. And it fails if the write tools ever move
    /// off the creation agent again, which would break the flow as quietly as the
    /// original defect and, unlike the original, on a path that writes.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_create_skill_instructs_calls_only_within_one_capability()
    {
        var text = File.ReadAllText(
            Path.Combine(RepoFiles.SkillsDir, "create-purchase-requisition.md"));

        using var scope = _provider!.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<ISpecialistCatalog>();

        var instructed = catalog.AllTools
            .Select(t => t.Name)
            .Where(n => Regex.IsMatch(text, $@"(?i)\bcall(?:ing|s)?\s+`?{Regex.Escape(n)}"))
            .ToList();

        Assert.NotEmpty(instructed);

        var owners = catalog.Specialists
            .Where(unit => unit.Tools.Any(t => instructed.Contains(t.Name, StringComparer.Ordinal)))
            .Select(unit => unit.Id)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            owners.Count == 1,
            $"data/skills/create-purchase-requisition.md tells the agent to call {string.Join(", ", instructed)}, "
            + $"which are owned by {owners.Count} capabilities ({string.Join(", ", owners)}). One skill must be "
            + "followable by one agent, or the model has to hand off mid-skill with nothing telling it to.");

        Assert.Equal(AgentIds.Creation, catalog.Specialists.Single(u => u.Id == owners[0]).AgentSlug);
    }

    /// <summary>
    /// A display rename must not be able to repoint routing or telemetry.
    /// </summary>
    /// <remarks>
    /// <c>AgentSlug</c> is init-only on purpose. A handoff resolves participants
    /// by <c>AIAgent.Id</c>, and telemetry is recorded under the same slug, so a
    /// setter would let a cosmetic change silently move both. Asserting the
    /// modifier is what makes the guarantee structural rather than a convention
    /// someone has to remember.
    /// </remarks>
    [Fact]
    public void A_display_rename_cannot_change_an_agent_slug()
    {
        using var scope = _provider!.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<ISpecialistCatalog>();

        var setter = typeof(SpecialistDefinition)
            .GetProperty(nameof(SpecialistDefinition.AgentSlug))!
            .SetMethod!;

        Assert.True(
            setter.ReturnParameter.GetRequiredCustomModifiers()
                .Any(m => m.Name == "IsExternalInit"),
            "SpecialistDefinition.AgentSlug must be init-only, or a display rename can repoint routing.");

        // The slug is the stable constant, not something derived from the label.
        var extracted = catalog.Specialists.Where(s => s.IsExtracted).ToList();
        Assert.Equal(
            new[] { AgentIds.Retrieval, AgentIds.Creation },
            extracted.Select(s => s.AgentSlug));
        Assert.All(extracted, s => Assert.NotEqual(s.DisplayName, s.AgentSlug));

        // Renaming the label leaves the slug, and therefore routing, untouched.
        // Checked for every extracted unit: the claim is that no rename can move
        // one, so a single representative proves less than the rule does.
        foreach (var unit in extracted)
        {
            var renamed = unit with { DisplayName = "Renamed for a demo" };
            Assert.Equal(unit.AgentSlug, renamed.AgentSlug);
            Assert.NotEqual(unit.DisplayName, renamed.DisplayName);
        }
    }

    /// <summary>
    /// Every reserved identity is claimed by exactly one unit, every unit claims
    /// one, and a unit is bound only to the identity it claims.
    /// </summary>
    /// <remarks>
    /// The slugs for the un-extracted capabilities used to be named only in prose
    /// on <see cref="AgentIds"/>, so a slug reserved for a future extraction was
    /// indistinguishable from one added by accident, and a unit that reserved an
    /// identity other than the one it was bound to would still have compiled.
    /// Reflection over the constants is what makes a newly added slug fail here
    /// instead of accumulating.
    /// </remarks>
    [Fact]
    public void Every_agent_identity_is_claimed_by_exactly_one_capability()
    {
        using var scope = _provider!.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<ISpecialistCatalog>();

        var units = catalog.Specialists.ToList();

        // No two units may reserve the same identity: they would become the same
        // agent, and the composer throws only once both are bound.
        var claimed = units.Select(u => u.ReservedAgentSlug).ToList();
        Assert.Equal(claimed.Count, claimed.Distinct(StringComparer.Ordinal).Count());

        // The orchestrator is the entry point, not an identity a capability may
        // reserve, or a unit could bind itself to the front door.
        Assert.DoesNotContain(AgentIds.Orchestrator, claimed);

        // Binding cannot repoint a unit at an identity it does not own.
        Assert.All(
            units.Where(u => u.IsExtracted),
            u => Assert.Equal(u.ReservedAgentSlug, u.AgentSlug));

        // Every declared identity is claimed, so a constant cannot sit unused
        // while looking deliberate, and the entry point aside, nothing else can
        // claim one.
        var declared = typeof(AgentIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f is { IsLiteral: true, FieldType.FullName: "System.String" })
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.NotEmpty(declared);
        Assert.All(declared.Except([AgentIds.Orchestrator]), slug =>
            Assert.Contains(slug, claimed));

        // The identity a unit will use once extracted is already decided, so
        // binding it later cannot change what its telemetry is filed under.
        var bound = units.Where(u => u.IsExtracted).Select(u => u.AgentSlug!).ToHashSet();
        var unclaimedButReserved = claimed.Where(slug => !bound.Contains(slug)).ToList();
        Assert.Equal(
            new[] { AgentIds.SkillActivation },
            unclaimedButReserved);
    }
}
