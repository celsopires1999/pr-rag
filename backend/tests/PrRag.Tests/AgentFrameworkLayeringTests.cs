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
    public async Task Purchase_requisition_tools_expose_the_five_fixed_tools()
    {
        using var scope = _provider!.CreateScope();
        var tools = scope.ServiceProvider.GetRequiredService<PurchaseRequisitionTools>();

        var names = tools.All.Select(t => t.Name).ToHashSet();

        Assert.Equal(5, tools.All.Count);
        Assert.True(names.IsSupersetOf(new[]
        {
            "search_by_codes",
            "search_semantic",
            "search_item_supplier_master",
            "activate_skill",
            "create_requisition",
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
}