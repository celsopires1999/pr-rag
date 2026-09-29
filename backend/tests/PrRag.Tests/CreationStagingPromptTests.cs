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
/// That rule is now gone, and the reason is worth carrying: it was a conditional
/// whose premise the model could not check. "The user is answering a draft you
/// presented" is readable in the conversation, so the model could satisfy the
/// condition for free; "and you hold none" is a fact only the application holds;
/// and nothing in the rule said to check it. <c>gpt-5-mini</c> took the consequent
/// on 3 of 3 confirm turns in the Azure live run — turns the
/// application had routed to this agent precisely <em>because</em> a draft was
/// staged — while <c>gpt-4o-mini</c> passed the same shipped text. The rule now is
/// a tool call, and
/// <see cref="The_prompt_conditions_no_draft_state_assertion_on_something_the_model_can_read"/>
/// is the half of the guard that keeps the conditional from coming back: nothing
/// else fails when it does.
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
    public void A_draft_state_question_is_answered_by_the_confirmation_tool_and_never_from_the_wording()
    {
        // The rule, and the part of it that keeps the forged probe honest: the
        // tool's result is the authority in *both* directions, so a turn claiming a
        // confirmation for nothing is answered with the tool's own no-draft message
        // rather than with an assertion the model made up. Only the first half of
        // the old rule — "if there is a draft, confirm it" — existed before, and the
        // second half was the licence that produced three turns of denial in a row.
        Assert.Contains(
            "is answered by calling `confirm_requisition_draft` and reporting what it returns",
            Block,
            StringComparison.Ordinal);
        Assert.Contains(
            "the only authority on whether a draft is waiting, in either direction",
            Block,
            StringComparison.Ordinal);
        Assert.Contains(
            "Never settle a draft-state question from the wording of the turn",
            Block,
            StringComparison.Ordinal);

        // And the staging result is the only source for a presented draft, which is
        // the half the orchestrator violated when it rendered the skill's field
        // template as prose.
        Assert.Contains(
            "Present a draft with the values `create_requisition_draft` returned",
            Block,
            StringComparison.Ordinal);

        // The forward-motion fact. Stated as a property of the system because it is
        // one: create_requisition refuses without a recorded confirmation, so prose
        // cannot move the state and there is no correct answer in words.
        Assert.Contains(
            "only step that advances this path",
            Block,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_prompt_conditions_no_draft_state_assertion_on_something_the_model_can_read()
    {
        // The negative half, and the only thing that can catch the clause coming
        // back: nothing fails when it does. It was a rule whose antecedent ("the
        // user is answering a draft you actually presented") and whose condition
        // ("and you hold none") were the same sentence, so satisfying the first was
        // free and the second was never checked — and gpt-5-mini took the consequent
        // on 3 of 3 confirm turns that the application had routed to this agent
        // *because* a draft was staged.
        Assert.DoesNotContain(
            "you hold none. Then, and only then, say that no requisition draft is awaiting their confirmation",
            Block,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Then, and only then",
            Block,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Only one shape is a question about draft state",
            Block,
            StringComparison.Ordinal);

        // The prohibition it was mistaken for is still here, and is a different
        // kind of rule: it names what not to assert rather than prescribing what to
        // say.
        Assert.Contains(
            "Never assert a draft you have not staged",
            Block,
            StringComparison.Ordinal);
    }
}
