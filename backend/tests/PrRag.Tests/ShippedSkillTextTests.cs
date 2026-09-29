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

    /// <summary>
    /// A step that presents an artifact comes after the step whose call produces it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The shipped skill had a six-line field template in step 5 — "present the
    /// draft as a structured summary … and explicitly ask the user to confirm" —
    /// sitting <em>before</em> the <c>create_requisition_draft</c> call that
    /// produces the draft. The template named no tool, so nothing in it could fail
    /// and nothing downstream contradicted it: the turn was renderable from the
    /// user's own sentence, and the staging call was optional. The live gate saw
    /// exactly that on the forged probe, 3 turns out of 3 on <c>gpt-5-mini</c> and
    /// 1 of 3 on the no-confirm probe, with the orchestrator's report reading
    /// <c>tools=['activate_skill']</c>, no handoff, and a draft in prose that no
    /// tool had staged.
    /// </para>
    /// <para>
    /// Compared on index rather than on the phrasing of any sentence, because the
    /// point is the order and a reword can satisfy a phrase check while keeping the
    /// defect. This is the check that class of defect has never had: a step that
    /// names no tool is the only kind nothing else can police.
    /// </para>
    /// <para>
    /// Scoped to the numbered procedure, which is where instructions live. The
    /// role statement above it names presentation as a goal and is not a step, so
    /// including it would make the assertion about the preamble rather than about
    /// the order the model is expected to follow.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_create_skill_presents_a_draft_only_after_the_call_that_produces_it()
    {
        var procedure = ProcedureOf(CreateSkill());
        var stage = procedure.IndexOf(ToolNames.CreateRequisitionDraft, StringComparison.Ordinal);

        Assert.True(stage >= 0, "The procedure no longer names the staging call, so the ordering cannot be checked.");

        foreach (var presentation in new[]
                 {
                     "present the draft",
                     "present a draft",
                     "ask the user to confirm",
                     "ask them to confirm",
                 })
        {
            var at = procedure.IndexOf(presentation, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                continue;
            }

            Assert.True(
                at > stage,
                $"data/skills/create-purchase-requisition.md tells the agent to '{presentation}' at position " +
                $"{at}, before the create_requisition_draft call at position {stage}. An artifact is presentable " +
                "only once the tool that produces it has returned one.");
        }
    }

    /// <summary>
    /// The skill carries no copy of the draft: no field template, no serialized
    /// example.
    /// </summary>
    /// <remarks>
    /// Both were a second, hand-maintained description of what
    /// <c>create_requisition_draft</c> produces, and the second copy had already
    /// drifted — the JSON block said <c>"ItemCode"</c> where the parameter is
    /// <c>item</c>. A field template is worse than stale, though: it names no tool,
    /// so it can be filled in from the user's own sentence and presented, and the
    /// drift is invisible because nothing checks a second copy against the first.
    /// <c>ToolDraftFields</c> is the authoritative shape and the tool returns it.
    /// </remarks>
    [Fact]
    public void The_create_skill_carries_no_field_template_and_no_serialized_draft()
    {
        var text = CreateSkill();

        Assert.False(
            text.Contains("```", StringComparison.Ordinal),
            "data/skills/create-purchase-requisition.md contains a fenced code block. A serialized draft " +
            "example is a second copy of the staging tool's schema, and it has already drifted (\"ItemCode\" " +
            "against the parameter item). The tool returns the values to present.");

        Assert.False(
            Regex.IsMatch(text, @"(?m)^\s*[-*]\s*(SupplierCode|Item|Description|Quantity|Date|Requester)\s*[:/]"),
            "data/skills/create-purchase-requisition.md contains a field template. A template names no tool, " +
            "so it can be rendered from the user's own message before anything has been staged.");

        // The values that reach the user come from the call, stated as such.
        Assert.Contains(
            "present the draft `create_requisition_draft` returned",
            text.ReplaceLineEndings(" "),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateSkill()
        => File.ReadAllText(Path.Combine(SkillsDir, "create-purchase-requisition.md"));

    /// <summary>
    /// The numbered procedure on its own, which is where the steps the model
    /// follows in order live.
    /// </summary>
    private static string ProcedureOf(string text)
    {
        var start = text.IndexOf("# Procedure", StringComparison.Ordinal);
        Assert.True(start >= 0, "data/skills/create-purchase-requisition.md no longer has a Procedure section.");

        var guardrails = text.IndexOf("# Guardrails", start, StringComparison.Ordinal);
        return guardrails < 0 ? text[start..] : text[start..guardrails];
    }
}
