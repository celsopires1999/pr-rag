using PrRag.Application.Services.Agents;
using Xunit;

namespace PrRag.Tests;

/// <summary>
/// The two pieces of handoff attribution that are pure functions, asserted
/// without a chat turn: resolving the framework's positional handoff name, and
/// what the turn context does with the result.
/// </summary>
/// <remarks>
/// Both are here rather than in <c>AgentGraphTopologyTests</c> because neither
/// needs a graph, a provider, or a database. A positional convention is exactly
/// the kind of thing that has to be pinned by a test cheap enough to be read,
/// because its failure mode is not a crash but a handoff attributed to the wrong
/// capability — which reads as a working report.
/// </remarks>
public class HandoffAttributionTests
{
    private static readonly string[] Participants = [AgentIds.Retrieval, AgentIds.Creation];

    /// <summary>
    /// The success path the rest of the system is built on: a generated name in,
    /// a capability slug out, for every position the graph can hold.
    /// </summary>
    [Fact]
    public void A_generated_handoff_name_resolves_to_the_capability_at_that_position()
    {
        for (var i = 0; i < Participants.Length; i++)
        {
            var name = HandoffToolName.ForParticipant(i + 1);

            Assert.Equal(Participants[i], HandoffToolName.ResolveTarget(name, Participants));
        }
    }

    /// <summary>
    /// The miss is a return value, not a throw, because the resolver runs on
    /// <em>every</em> function call the model makes and most of them are ordinary
    /// tools. Throwing there would fail a turn for calling <c>search_by_codes</c>.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("search_by_codes")]
    [InlineData("handoff_to")]
    [InlineData("handoff_to_x")]
    [InlineData("handoff_to_0")]
    [InlineData("handoff_to_-1")]
    [InlineData("Handoff_to_1")]
    public void A_name_that_is_not_a_handoff_in_this_graph_resolves_to_nothing(string? toolName)
    {
        Assert.Null(HandoffToolName.ResolveTarget(toolName, Participants));
    }

    /// <summary>
    /// A recognised name with a position past the end is the graph and the
    /// convention disagreeing, and it throws.
    /// </summary>
    /// <remarks>
    /// The only alternative is resolving it to some other capability, which is
    /// worse than a failed turn: the report would then say the orchestrator
    /// delegated to creation when the call named a participant that does not
    /// exist, and the routing defect this whole mechanism exists to diagnose
    /// would be misattributed. It is also the one branch no in-process turn can
    /// reach — the fake only ever emits a name the catalog implies — so without
    /// this the throw would be untested code guarding a real divergence.
    /// </remarks>
    [Fact]
    public void A_recognised_name_beyond_the_last_participant_is_an_error()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => HandoffToolName.ResolveTarget(HandoffToolName.ForParticipant(3), Participants));

        Assert.Contains("handoff_to_3", ex.Message, StringComparison.Ordinal);
        Assert.Contains("2 participant(s)", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// One delegation is one entry, however many times the call is observed.
    /// </summary>
    /// <remarks>
    /// A streamed function call arrives as one update carrying the name and
    /// further updates carrying argument deltas, all of them the same
    /// <c>FunctionCallContent</c>. Counting those separately would report a
    /// fraction of a handoff as several delegations, and the live gate reads this
    /// list to decide whether a turn routed at all.
    /// </remarks>
    [Fact]
    public void A_delegation_observed_more_than_once_is_recorded_once()
    {
        var turn = new AgentTurnContext();

        turn.RecordHandoff(AgentIds.Orchestrator, AgentIds.Creation);
        turn.RecordHandoff(AgentIds.Orchestrator, AgentIds.Creation);

        var handoff = Assert.Single(turn.Handoffs);
        Assert.Equal(AgentIds.Orchestrator, handoff.From);
        Assert.Equal(AgentIds.Creation, handoff.To);
    }

    /// <summary>
    /// Two different delegations are two entries, in the order they were taken,
    /// and neither collapses into the other.
    /// </summary>
    [Fact]
    public void Two_delegations_are_both_kept_in_call_order()
    {
        var turn = new AgentTurnContext();

        turn.RecordHandoff(AgentIds.Orchestrator, AgentIds.Creation);
        turn.RecordHandoff(AgentIds.Orchestrator, AgentIds.Retrieval);
        turn.RecordHandoff(AgentIds.Creation, AgentIds.Retrieval);

        Assert.Equal(
            new[]
            {
                (AgentIds.Orchestrator, AgentIds.Creation),
                (AgentIds.Orchestrator, AgentIds.Retrieval),
                (AgentIds.Creation, AgentIds.Retrieval),
            },
            turn.Handoffs.Select(h => (h.From, h.To)));
    }

    /// <summary>
    /// The turn context is scoped per turn, and <see cref="AgentTurnContext.Begin"/>
    /// is what makes it so. Without the reset a second turn in the same session
    /// would inherit the first turn's delegations, and the report would name
    /// handoffs that turn never took.
    /// </summary>
    [Fact]
    public void Beginning_a_turn_discards_the_previous_turns_delegations()
    {
        var turn = new AgentTurnContext();
        var state = new AgentSessionState();

        turn.Begin(state, "session-a", topK: 5, minSimilarity: 0.7);
        turn.RecordHandoff(AgentIds.Orchestrator, AgentIds.Creation);
        Assert.Single(turn.Handoffs);

        turn.Begin(state, "session-a", topK: 5, minSimilarity: 0.7);

        Assert.Empty(turn.Handoffs);
    }
}
