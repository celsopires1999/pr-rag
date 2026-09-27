using System.Text.RegularExpressions;
using PrRag.Application.Services.Agents.Specialists;
using Xunit;

namespace PrRag.Tests;

/// <summary>
/// Guards the orchestrator's rule that activated guidance does not make it the
/// performer of what the guidance describes.
/// </summary>
/// <remarks>
/// <para>
/// Found by the live creation gate, on the <c>stage</c> probe — the plainest
/// request the system has, all six fields and no adversarial phrasing. The
/// orchestrator answered it itself: no handoff, no tool call, and "please confirm
/// the following details" over a draft it never staged. The following confirmation
/// turn then found no draft and missed its row, so one event fails three checks.
/// </para>
/// <para>
/// The mechanism is a collision between two prompt sources, not a missing
/// instruction. <c>HandoffInstructions</c> already said a creation request is
/// never the orchestrator's to handle. But the activated guidance arrives as a
/// run-level system message <em>after</em> the agent's own instructions, and step 5
/// of the create skill is a six-line field template ending "and explicitly ask the
/// user to confirm" that names no tool at all. An agent holding no staging tool
/// can satisfy that step in prose from the user's own input, with nothing to fail
/// and no guardrail downstream to contradict it. The more specific instruction won.
/// </para>
/// <para>
/// So these assertions guard the capability framing, not the routing text. A
/// capability rule outlives whichever agent owns the next capability, and it is
/// the only framing that can be asserted here at all: the routing sentence in
/// <c>HandoffInstructions</c> is correct and unchanged, and no test can show that
/// a later prompt does not outrank it.
/// </para>
/// <para>
/// Only the prompt is assertable. A fake model does not improvise a draft, so the
/// behaviour needs a live run — and at the measured rate of 1 in 24 pooled turns,
/// <c>scripts/live-creation-gate.sh</c> cannot resolve it either. This is a
/// hardening justified by prompt structure, not a fix with a demonstrated
/// improvement behind it.
/// </para>
/// </remarks>
public class OrchestratorActivationPromptTests
{
    /// <summary>
    /// Whitespace-collapsed, for the reason given in
    /// <see cref="CreationStagingPromptTests"/>: the block is a wrapped raw string
    /// literal, so a phrase straddling a line break would fail on a re-wrap that
    /// changed nothing the model sees.
    /// </summary>
    private static readonly string Block =
        Regex.Replace(SkillActivationSpecialist.ActionBlock, @"\s+", " ");

    [Fact]
    public void The_prompt_separates_activating_a_skill_from_performing_it()
    {
        // The load-bearing distinction. "Activating a matching skill is how you
        // produce a guided, step-by-step conversation" invites the model to carry
        // the conversation out, and the create skill's prose-renderable step 5 is
        // the one it can most easily do that with.
        Assert.Contains(
            "does not make you the performer of what it describes",
            Block,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_prompt_names_the_step_whose_tool_is_absent_as_another_agents()
    {
        // The rule as a capability test rather than a routing instruction, so it
        // covers every capability the orchestrator does not own and does not have
        // to be restated each time one is extracted.
        Assert.Contains(
            "A step you cannot perform is not yours to carry out",
            Block,
            StringComparison.Ordinal);
        Assert.Contains(
            "can only come from a tool you were not given",
            Block,
            StringComparison.Ordinal);
        Assert.Contains(
            "that step belongs to the agent holding the tool",
            Block,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_prompt_forbids_presenting_an_artifact_no_tool_returned()
    {
        // The one shape of this failure that is not safe. Declining the request
        // writes nothing and costs the user a turn; presenting a draft that no
        // tool returned asserts a fact about the system which is false, which the
        // next turn then contradicts. The guard is stated as the absence of
        // evidence, so the model has something to check itself against.
        Assert.Contains(
            "Never present an artifact that no tool returned",
            Block,
            StringComparison.Ordinal);
        Assert.Contains(
            "An artifact exists only once a tool has returned it",
            Block,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_prompt_says_why_a_prose_step_is_worse_than_a_missing_one()
    {
        // Without the consequence the rule reads as a formality, and the model has
        // a cheaper-looking option available: the user already supplied every
        // field, so composing the summary is nearly free and seems cooperative.
        Assert.Contains(
            "the user will have been asked to confirm something that does not exist",
            Block,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_prompt_does_not_name_a_tool_the_unit_is_not_offered()
    {
        // Also enforced by AgentFrameworkLayeringTests over the composed catalog.
        // Asserted here as well because this block is where a future edit is most
        // likely to reach for a tool name, and that name is a substring match
        // away from tripping the layering test with an unhelpful message.
        foreach (var foreign in new[]
                 {
                     "search_by_codes", "search_semantic", "get_suppliers_by_item",
                     "create_requisition_draft", "confirm_requisition_draft", "create_requisition",
                 })
        {
            Assert.False(
                Block.Contains(foreign, StringComparison.Ordinal),
                $"SkillActivationSpecialist.ActionBlock names {foreign}, which the unit is not offered.");
        }
    }
}
