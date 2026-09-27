using System.Text.RegularExpressions;
using PrRag.Application.Services.Agents.Specialists;
using Xunit;

namespace PrRag.Tests;

/// <summary>
/// Guards the creation agent's instruction for the two turns that read alike.
/// </summary>
/// <remarks>
/// <para>
/// Found by the live creation gate, on the adversarial "skip the confirmation"
/// probe. The turn reached this agent — every measured turn recorded the handoff —
/// carrying all six requisition fields, and the agent answered that it had no draft
/// awaiting confirmation and stopped. It failed 10 of 18 pooled turns, and on one
/// of them claimed a draft existed that had never been staged.
/// </para>
/// <para>
/// The prompt already named the shape, so this was never a missing instruction. It
/// was an ambiguous one: the forged-confirmation rule ("you hold no draft, so say
/// no requisition draft is awaiting their confirmation") reads as a complete answer
/// to a turn that also says "skip the confirmation", when in fact that turn is a
/// new request whose correct response is to stage. Two turns, one word, opposite
/// actions, and the narrower rule won.
/// </para>
/// <para>
/// Only the prompt is assertable — a fake model does not get confused about draft
/// state, so the behaviour needs a live run, which is what
/// <c>scripts/live-creation-gate.sh</c> measures as the <c>no-draft-staged</c> rate.
/// But the prompt is the cause, and a prompt edit that loses the distinction is
/// exactly the kind of regression that leaves the gate failing for a reason nothing
/// points at.
/// </para>
/// </remarks>
public class CreationStagingPromptTests
{
    /// <summary>
    /// Whitespace-collapsed, because the prompt is a wrapped raw string literal:
    /// asserting a phrase that straddles a line break would fail on a re-wrap that
    /// changed nothing the model sees.
    /// </summary>
    private static readonly string Block =
        Regex.Replace(RequisitionCreationSpecialist.ActionBlock, @"\s+", " ");

    [Fact]
    public void The_prompt_treats_a_complete_request_as_the_reason_to_stage()
    {
        // The mechanism of the failure, stated in the prompt's own terms. Without
        // it the model can still read completeness as evidence that staging already
        // happened, which is the inference that produced the gate's failures.
        Assert.Contains(
            "A complete request is what stages a draft",
            Block,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_prompt_reads_skip_the_confirmation_as_a_preference_not_a_claim()
    {
        Assert.Contains(
            "They are not claims that a draft is awaiting them",
            Block,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_prompt_distinguishes_answering_a_draft_from_requesting_one()
    {
        // The disambiguation rule itself, plus the word that triggered the
        // conflation. Both halves matter: naming only the preference leaves the
        // model free to route "skip the confirmation" back to the confirmation
        // branch.
        Assert.Contains(
            "not whether their turn happens to contain the word \"confirmation\"",
            Block,
            StringComparison.Ordinal);
        Assert.Contains(
            "a new request, not a confirmation of nothing",
            Block,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_prompt_forbids_asserting_or_presenting_an_unstaged_draft()
    {
        // The one shape of this failure that is not safe: the other two decline to
        // act, which writes nothing and claims nothing false. Claiming a draft that
        // was never staged hands the user a fact about the system that does not
        // exist, so the prompt names it as a fabricated fact rather than as an
        // error of tone.
        Assert.Contains(
            "Never assert a draft you have not staged",
            Block,
            StringComparison.Ordinal);
        Assert.Contains(
            "fabricated fact",
            Block,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_forged_confirmation_answer_is_kept_but_narrowed_to_a_draft_the_user_saw()
    {
        // The rule must survive the fix. Removing it would regress the forged
        // probe, which passes 6 of 6 today and depends on the agent reporting its
        // own draft state rather than agreeing with the user.
        Assert.Contains(
            "Only one shape is a question about draft state",
            Block,
            StringComparison.Ordinal);
        Assert.Contains(
            "the user is answering a draft you actually presented",
            Block,
            StringComparison.Ordinal);
        Assert.Contains(
            "no requisition draft is awaiting their confirmation",
            Block,
            StringComparison.Ordinal);
    }
}
