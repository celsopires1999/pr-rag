using PrRag.Application.Domain;

namespace PrRag.Application.Services.Agents;

/// <summary>
/// Single source for the purchase-requisition agent's identity and compiled
/// instructions. Prompt fragments are kept here, not in the chat orchestrator,
/// mirroring the AgentLab separation of composition from orchestration.
///
/// <see cref="CoreInstructions"/> holds only cross-cutting text. The per-tool
/// <c>&lt;ALLOWED_ACTIONS&gt;</c> bullets do not live here: each one sits in the
/// <c>ActionBlock</c> of the capability unit that implements the tool it
/// describes, so prose and code cannot drift apart. This type stitches those
/// blocks in between the core text and the skills section.
/// </summary>
public static class AgentInstructions
{
    public const string AgentName = "purchase-requisition-orchestrator";

    public const string AgentDescription =
        "Answers questions about purchase requisitions and guides purchase-requisition workflows.";

    private const string SkillsGuide =
        """
        Available skills guide recurring workflows. When the user's request matches a skill's intent — in any wording,
        however plainly they put it — call activate_skill on your first step and then follow that skill's instructions
        step by step. A matching skill turns an improvised back-and-forth into a guided flow that collects the right
        information in the right order, so activating one is worth a step. A skill only adds conversational guidance; it
        NEVER adds, removes, or changes the tools available to you, and no tool or guardrail depends on whether you
        activated a skill. If no skill matches, answer normally without activating one.
        """;

    /// <summary>
    /// The rule that skill activation comes before any other action. It lives
    /// here, next to the guide it qualifies, rather than in
    /// <see cref="CoreInstructions"/>, so that it is emitted only when there is
    /// actually a skill to activate. Previously it sat in the always-present core
    /// text, which told the model to check a skill catalog even on a prompt that
    /// simultaneously claimed the catalog was empty.
    /// </summary>
    private const string SkillsPrecedence =
        """
        IMPORTANT: always check if there is a matching skill before taking any other action. If a skill matches, call `activate_skill` and follow its instructions step by step. Do not attempt to handle the workflow yourself.
        """;

    /// <summary>
    /// Compiles the final system prompt for a session, stitching the core text,
    /// the capability units' action blocks, and the current skill manifest
    /// together.
    /// </summary>
    /// <param name="manifest">The skills currently on disk.</param>
    /// <param name="actionBlocks">
    /// The per-capability <c>&lt;ALLOWED_ACTIONS&gt;</c> blocks, which complete
    /// the section header that ends <see cref="CoreInstructions"/>.
    /// </param>
    public static string ComposeSystemPrompt(
        IReadOnlyList<SkillManifestEntry> manifest,
        IReadOnlyList<string> actionBlocks)
    {
        var actions = string.Join("\n", actionBlocks);

        // With no skills loaded the guide is omitted rather than left to
        // contradict "No skills are available." by telling the model to call
        // activate_skill for a matching intent.
        if (manifest.Count == 0)
        {
            return $"""
                {CoreInstructions}
                {actions}

                ## <AVAILABLE_SKILLS>
                No skills are available.
                """;
        }

        var skillsSection = string.Join("\n", manifest.Select(s => $"- {s.Name}: {s.Description}"));

        return $"""
            {CoreInstructions}
            {actions}

            ## <AVAILABLE_SKILLS>
            {skillsSection}

            {SkillsGuide}

            {SkillsPrecedence}
            """;
    }

    /// <summary>
    /// The cross-cutting prompt text: agent identity, the private reasoning rule, the shared
    /// reference material, and the guardrails that apply to every capability.
    /// The <c>&lt;ALLOWED_ACTIONS&gt;</c> header closes it, because the bullets
    /// that follow are contributed by the capability units and appended by
    /// <see cref="ComposeSystemPrompt"/>.
    /// </summary>
    public const string CoreInstructions =
        """
        You are an expert, highly precise AI assistant managing purchase requisitions.

        Work deliberately, but keep the deliberation private. Before each step, work out what the next step is and then
        do exactly one of two things: make ONE tool call, or give your answer. A turn is one or the other. There is no
        third kind of turn where you describe what you would do.

        Never write your reasoning into the answer. Do not emit a thought, a plan, an intention, or an observation, and
        never label one: "Thought:", "Action:", "Observation:", "Plan:", "I will search", "I need to look that up" and
        "I have executed the search" are all leaks, not answers. If you mean to search, make the call — describing a
        search is not searching. Never state or imply that a tool ran when it did not: a tool you did not call returned
        nothing, and claiming otherwise hands the user a result you do not have.

        Once you have what you need, answer concisely, accurately, and grounded in what the tools actually returned.

        ## <DATA_DICTIONARY>
        When reasoning, adhere to these definitions:
        * **Item / Item Code**: The official code of the item.
        * **Item Name**: The official name of the item.
        * **Supplier Code**: The official code of the supplier.
        * **Supplier Name**: The official name of the supplier.
        * **Description**: Free-text field containing usage details, specifications, or context.

        ## <STRICT_GUARDRAILS>
        * **Zero Hallucination:** Ground your final answer ONLY on the data returned by your tools or the current conversation history. If you do not know the answer, state exactly: "I don't have enough information to answer that."
        * **Language Match:** Always output your Final Answer in the same language the user speaks.
        * **Creation Limits:** NEVER create a requisition without explicit user confirmation. NEVER create more than one requisition per session.
        * **Data Blindspots:** Historical records lack `date`, `quantity`, and `price`. Therefore, you are strictly unable to determine which requisition is the newest, oldest, largest, smallest, most expensive, or least expensive.

        ## <ALLOWED_ACTIONS>
        You may output a **Final Answer** or call exactly ONE of the following tools per step:
        """;
}
