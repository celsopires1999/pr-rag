using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using PrRag.Application.Abstractions;
using PrRag.Application.Domain;
using PrRag.Application.DTOs;
using PrRag.Infrastructure.Persistence;
using Xunit;

namespace PrRag.Tests;

/// <summary>
/// Guards WHERE the compiled system prompt is delivered to the chat model.
///
/// The prompt used to be assembled by <c>ChatService</c> and added as a
/// <c>ChatRole.System</c> message on a session's first turn only, which meant
/// the skill manifest was frozen for the life of the session. It now travels on
/// the agent's own instructions. That is only safe if the framework does not
/// also persist the injected instruction into accumulated history — otherwise
/// every turn appends another copy of a ~2.5k-token prompt, which no other test
/// in the suite would detect.
///
/// So this file asserts the invariant directly: the manifest section appears in
/// the messages the model receives exactly once, on every turn.
///
/// TWO FACTS, both measured rather than assumed, and together they are the whole
/// reason this migration is safe.
///
/// 1. Pre-migration, MAF's default ChatHistoryProvider *persists* the system
///    message into accumulated history. That is why the manifest used to survive
///    follow-up turns even though it was only sent when the session was created,
///    and the measured baseline was 1 on turn 1, 1 on turn 2, and 1 per new
///    session. There was no pre-existing duplication.
///
/// 2. Post-migration, `ChatClientAgent` does NOT put an agent's `Instructions`
///    into the message list. It passes them as `ChatOptions.Instructions`
///    (verified directly against the agent, not inferred). Two things follow:
///    the prompt is rebuilt on every run, so a skill manifest reloaded mid-session
///    reaches the very next turn; and because that channel is not part of the
///    message history, it cannot stack up across turns. The duplication hazard
///    that a naive reading of (1) suggests — a history copy sitting beside a
///    freshly injected one — does not exist.
///
/// `FakeChatClient` folds `ChatOptions.Instructions` into `LastPrompt`, without
/// which these assertions would read 0 and look like the prompt had vanished.
/// </summary>
public class SystemPromptChannelTests : IAsyncLifetime
{
    // The manifest *body* marker, not the section header. CoreInstructions itself
    // contains a backticked "<AVAILABLE_SKILLS>" inside the activate_skill bullet,
    // so counting the header string would also count that prose reference and
    // report 2 on a clean turn 1. The "- {name}: {description}" entry line appears
    // only where the catalog is actually rendered.
    private const string ManifestEntry = "- create-purchase-requisition:";

    private const string SkillFile = """
        ---
        name: create-purchase-requisition
        description: Use this skill ONLY when the user wants to CREATE a new purchase requisition or draft a new one.
        version: 1
        ---
        # Role
        When this skill is active you act as a purchasing assistant that helps the user draft a new purchase requisition.
        """;

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
        };
        await WriteJsonAsync(records);

        // At least one loaded skill, so the manifest renders real content rather
        // than the empty-catalog branch.
        var skillsDir = Path.Combine(_dataDir, "skills");
        Directory.CreateDirectory(skillsDir);
        await File.WriteAllTextAsync(Path.Combine(skillsDir, "create-purchase-requisition.md"), SkillFile);

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

    private async Task WriteJsonAsync(IEnumerable<PurchaseRequisitionImport> records)
    {
        var settings = _provider!.GetRequiredService<Microsoft.Extensions.Options.IOptions<PrRag.Application.Configuration.DataSettings>>();
        var json = System.Text.Json.JsonSerializer.Serialize(records);
        await File.WriteAllTextAsync(settings.Value.FilePath, json);
    }

    /// <summary>
    /// Counts manifest entries in everything the model was given. Read from
    /// <see cref="FakeChatClient.LastPrompt"/> rather than from the message list,
    /// because after the migration the prompt arrives as
    /// <c>ChatOptions.Instructions</c> and is not a system message at all.
    /// </summary>
    private static int ManifestCount(FakeChatClient chatClient) =>
        CountOccurrences(chatClient.LastPrompt, ManifestEntry);

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    [Fact]
    public async Task The_manifest_is_present_exactly_once_on_every_turn()
    {
        var sessionId = Guid.NewGuid().ToString("N");
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        // Turn 1 creates the session, so the prompt is delivered for the first time.
        var first = await chat.AnswerAsync(new ChatRequest
        {
            SessionId = sessionId,
            Question = "What does ITM0001 describe?",
            TopK = 5,
            MinSimilarity = 0,
        });
        Assert.NotEmpty(first.Answer);
        Assert.Equal(1, ManifestCount(chatClient));

        // Turn 2 reuses the session. The manifest must still be delivered, and
        // must not have been duplicated by history accumulation.
        var streamed = new List<string>();
        await foreach (var token in chat.StreamAsync(new ChatStreamRequest
        {
            SessionId = sessionId,
            Question = "And which suppliers provided it?",
            TopK = 5,
            MinSimilarity = 0,
        }))
        {
            streamed.Add(token);
        }

        Assert.NotEmpty(streamed);
        Assert.Equal(1, ManifestCount(chatClient));
    }

    /// <summary>
    /// The spec drift this migration closes: the skill manifest used to be
    /// rendered once, when a session was created, and then frozen into that
    /// session's history. A skill added or edited afterwards never reached the
    /// user, even on their next message. Now the prompt is rebuilt per request,
    /// so a reload reaches a session that is already in flight.
    ///
    /// Turn 2 runs in a fresh scope because that is what a real second request
    /// is — <c>AgentRunService</c> is scoped and composes the prompt when it is
    /// constructed. The session itself survives, because the session store is a
    /// singleton; that is the whole point of "in-flight".
    ///
    /// Pre-migration this could not have passed: the system message was added
    /// only when a session was created, and the turn-1 manifest measured in
    /// task 1.2 was therefore frozen into history listing only the first skill.
    /// (That is a consequence of the measured baseline plus the old
    /// `if (created)` guard, not a run of this test against the old code.)
    /// </summary>
    [Fact]
    public async Task A_manifest_reload_reaches_an_in_flight_session_on_its_next_turn()
    {
        const string addedSkillEntry = "- reconcile-invoices:";

        var sessionId = Guid.NewGuid().ToString("N");

        using (var firstScope = _provider!.CreateScope())
        {
            var chat = firstScope.ServiceProvider.GetRequiredService<IChatService>();
            var chatClient = firstScope.ServiceProvider.GetRequiredService<FakeChatClient>();

            await chat.AnswerAsync(new ChatRequest
            {
                SessionId = sessionId,
                Question = "What does ITM0001 describe?",
                TopK = 5,
                MinSimilarity = 0,
            });

            Assert.Equal(1, ManifestCount(chatClient));
            Assert.Contains(ManifestEntry, chatClient.LastPrompt);
            Assert.DoesNotContain(addedSkillEntry, chatClient.LastPrompt);
        }

        // A watcher-style reload: a new skill lands on disk, then the directory
        // is re-read.
        await File.WriteAllTextAsync(
            Path.Combine(_dataDir, "skills", "reconcile-invoices.md"),
            """
            ---
            name: reconcile-invoices
            description: Guides matching invoices to requisitions.
            version: 1
            ---
            # Role
            You reconcile invoices against requisitions.
            """);

        using (var reloadScope = _provider.CreateScope())
        {
            await reloadScope.ServiceProvider.GetRequiredService<ISkillService>().ReloadAsync();
        }

        using (var secondScope = _provider.CreateScope())
        {
            var chat = secondScope.ServiceProvider.GetRequiredService<IChatService>();
            var chatClient = secondScope.ServiceProvider.GetRequiredService<FakeChatClient>();

            await chat.AnswerAsync(new ChatRequest
            {
                SessionId = sessionId,
                Question = "And which suppliers provided it?",
                TopK = 5,
                MinSimilarity = 0,
            });

            // The reloaded skill is now offered, the original is still offered,
            // and neither is duplicated.
            Assert.Contains(addedSkillEntry, chatClient.LastPrompt);
            Assert.Contains(ManifestEntry, chatClient.LastPrompt);
            Assert.Equal(2, ManifestCount(chatClient) + CountOccurrences(chatClient.LastPrompt, addedSkillEntry));
        }
    }

    [Fact]
    public async Task A_separate_session_gets_its_own_single_manifest()
    {
        using var scope = _provider!.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
        var chatClient = scope.ServiceProvider.GetRequiredService<FakeChatClient>();

        foreach (var _ in Enumerable.Range(0, 3))
        {
            var response = await chat.AnswerAsync(new ChatRequest
            {
                Question = "What does ITM0001 describe?",
                TopK = 5,
                MinSimilarity = 0,
            });
            Assert.NotEmpty(response.Answer);
            Assert.Equal(1, ManifestCount(chatClient));
        }
    }
}
