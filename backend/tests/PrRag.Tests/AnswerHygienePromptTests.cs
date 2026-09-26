using System.Text.RegularExpressions;
using PrRag.Application.Services.Agents;
using Xunit;

namespace PrRag.Tests;

/// <summary>
/// Guards the answer-hygiene rule in the shared core prompt.
/// </summary>
/// <remarks>
/// <para>
/// Found by the live gate. The core prompt used to mandate a literal ReAct trace
/// — "you MUST strictly follow this continuous reasoning loop: 1. Thought: ...
/// 2. Action: ... 3. Observation: ..." — and then, four lines later, say "do not
/// output any reasoning or observations to the user". That is a contradiction,
/// and the model resolved it by sometimes printing the labels.
/// </para>
/// <para>
/// Measured on the semantic path, where the model has the most reasoning to do:
/// 3 turns in 8 leaked "Thought:/Action:/Observation:" into the user-visible
/// answer. Two shapes, and the second is not cosmetic — the model narrated all
/// three labels, wrote "Observation: Executed the search", and returned
/// <c>ret=0</c> with no tool call at all. That hands the user a claimed result
/// that does not exist. After the prompt was rewritten the same measurement gave
/// 0 in 20.
/// </para>
/// <para>
/// This cannot be asserted behaviourally: a fake model does not narrate, so only
/// a live run can catch the behaviour. What is assertable is the prompt, and the
/// prompt is the cause. If the labelled loop comes back, this fails.
/// </para>
/// </remarks>
public class AnswerHygienePromptTests
{
    /// <summary>
    /// Whitespace-collapsed, because the prompt is a wrapped raw string literal:
    /// asserting a phrase that happens to straddle a line break would fail on a
    /// re-wrap that changed nothing the model sees.
    /// </summary>
    private static readonly string Core =
        Regex.Replace(AgentInstructions.CoreInstructions, @"\s+", " ");

    [Fact]
    public void The_core_prompt_does_not_mandate_a_labelled_reasoning_trace()
    {
        // The old text required these as numbered steps, so the model produced
        // them as content. Requiring the labels is what caused the leak.
        foreach (var mandate in new[]
        {
            "ReAct",
            "you MUST strictly follow",
            "1. **Thought:**",
            "2. **Action:**",
            "3. **Observation:**",
        })
        {
            Assert.False(
                Core.Contains(mandate, StringComparison.OrdinalIgnoreCase),
                $"CoreInstructions mandates '{mandate}'. Mandating labelled reasoning sections is what made " +
                "the model print them into user-visible answers.");
        }
    }

    [Fact]
    public void The_core_prompt_forbids_narration_and_claiming_an_unmade_tool_call()
    {
        // The instruction that replaced the ReAct mandate, and the one that
        // actually addresses the fabricated-completion variant.
        Assert.Contains("Never write your reasoning into the answer", Core, StringComparison.Ordinal);
        Assert.Contains("describing a search is not searching", Core, StringComparison.Ordinal);
        Assert.Contains(
            "Never state or imply that a tool ran when it did not",
            Core,
            StringComparison.Ordinal);
    }
}
