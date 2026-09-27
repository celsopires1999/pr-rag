using System.Text.RegularExpressions;
using PrRag.Application.Services.Agents;
using Xunit;

namespace PrRag.Tests;

/// <summary>
/// Guards on the skill markdown that actually ships in <c>data/skills</c>, not on
/// a fixture.
/// </summary>
/// <remarks>
/// The skill text is prompt input, so a wrong instruction in it is a silent
/// behavioural bug: the model is told to do something the agent cannot do, and
/// the only symptom is that the step silently never happens. The live gate hit
/// exactly this — the create skill told the orchestrator to validate codes with
/// <c>search_by_codes</c>, which the orchestrator does not own, so validation was
/// skipped on every creation turn and nothing failed. A fixture cannot catch that
/// class of bug, because the fixture is not what ships.
/// </remarks>
public class ShippedSkillTextTests
{
    private static string SkillsDir => RepoFiles.SkillsDir;

    [Fact]
    public void The_create_skill_never_instructs_the_agent_to_call_a_tool_it_does_not_own()
    {
        var text = File.ReadAllText(Path.Combine(SkillsDir, "create-purchase-requisition.md"));

        // The read tools belong to the retrieval participant and the write tools
        // to the creation participant, so prose telling the agent to "call" one of
        // the other unit's is an instruction the agent cannot follow. Which unit is
        // "the" agent moves with the extraction; the rule does not, and neither
        // does the failure. See
        // The_create_skill_instructs_calls_only_within_one_capability for the
        // ownership check that survives the extraction.
        foreach (var foreign in new[] { ToolNames.SearchByCodes, ToolNames.SearchSemantic, ToolNames.GetSuppliersByItem })
        {
            foreach (Match call in Regex.Matches(text, $@"(?i)\bcall(?:ing|s)?\s+`?{Regex.Escape(foreign)}"))
            {
                // A mention is fine when it is a disclaimer: the shipped skill
                // says the agent *cannot* use these. An instruction is not.
                var line = text[..call.Index].Split('\n').Last();
                Assert.False(
                    !line.Contains("cannot", StringComparison.OrdinalIgnoreCase)
                    && !line.Contains("belongs to another agent", StringComparison.OrdinalIgnoreCase),
                    $"data/skills/create-purchase-requisition.md instructs the agent to call '{foreign}', " +
                    "which the agent running the creation flow does not own. Say the check is done by " +
                    "create_requisition instead.");
            }
        }
    }

    [Fact]
    public void The_create_skill_does_not_steer_routing()
    {
        var text = File.ReadAllText(Path.Combine(SkillsDir, "create-purchase-requisition.md"));

        // Routing belongs to the action block beside the tools, not to skill
        // text: the graph decides who can reach whom, and a skill that talks
        // about specialists drifts from the graph without failing anything.
        foreach (var phrase in new[] { "retrieval specialist", "hand that question", "route to", "delegate to", "hand off" })
        {
            Assert.False(
                text.Contains(phrase, StringComparison.OrdinalIgnoreCase),
                $"data/skills/create-purchase-requisition.md steers routing ('{phrase}'). " +
                "Routing guidance belongs in the owning capability's action block.");
        }
    }

    [Fact]
    public void The_create_skill_does_not_make_activation_a_precondition_for_a_tool()
    {
        var text = File.ReadAllText(Path.Combine(SkillsDir, "create-purchase-requisition.md"));

        // The guardrails must hold with the skill inactive, so nothing may make
        // activation a precondition for acting.
        foreach (var phrase in new[] { "only after activating", "must first activate", "you must activate" })
        {
            Assert.False(
                text.Contains(phrase, StringComparison.OrdinalIgnoreCase),
                $"data/skills/create-purchase-requisition.md makes activation a precondition ('{phrase}').");
        }

        Assert.Contains("whether or not this skill is active", text, StringComparison.OrdinalIgnoreCase);
    }
}
