using System.ComponentModel;
using Microsoft.Extensions.AI;
using System.Diagnostics;
using PrRag.Application.Abstractions;
using PrRag.Application.DTOs;

namespace PrRag.Application.Services.Agents.Specialists;

/// <summary>
/// Skill-activation capability: the single <c>activate_skill</c> tool.
///
/// Activating a skill only loads conversational guidance into the session; it
/// never changes the tool set. That is why this unit owns one tool and carries
/// no write path, and it is the reason Phase 2 can delegate to it without
/// ordering constraints against the creation capability.
/// </summary>
public sealed class SkillActivationSpecialist
{
    public const string Id = "purchase-requisition-skill-activation";

    public const string DisplayName = "Purchase-requisition skill activation";

    /// <summary>
    /// The action bullet for this unit's tool, moved verbatim out of
    /// <see cref="AgentInstructions.CoreInstructions"/>.
    /// </summary>
    public const string ActionBlock =
        """
        * **`activate_skill`**: You MUST call this whenever the user's request matches a skill listed in `<AVAILABLE_SKILLS>`, in ANY wording. This includes the plainest phrasings — "I need to create a purchase requisition", "create a new requisition", "draft a requisition", "I want to place a purchase request" — not only requests that name the skill. Activating a matching skill is how you produce a guided, step-by-step conversation instead of improvising one, so do it on the first step rather than trying to handle the workflow yourself.
        * **A step you cannot perform is not yours to carry out**: skill guidance describes steps for the agent that owns the capability each step acts on. Activating a skill loads that guidance and hands you nothing else — it does not make you the performer of what it describes — and the guidance is delivered at run level, so every agent in the run reads it. When a step's result can only come from a tool you were not given, that step belongs to the agent holding the tool: hand the turn over rather than performing the step yourself.
        * **Never present an artifact that no tool returned**: you have no basis for writing a draft, a summary, a table, or a request for the user to confirm, out of the user's own input or out of what the guidance describes. An artifact exists only once a tool has returned it, and describing one that no tool produced states a fact the system does not hold: the next turn will contradict you, and the user will have been asked to confirm something that does not exist. If you have no tool result, you have nothing to present.
        """;

    private readonly ISkillService _skillService;
    private readonly AgentTurnContext _turnContext;
    private readonly SpecialistToolSet _tools;
    private readonly List<AITool> _ownedTools = [];

    public SkillActivationSpecialist(
        ISkillService skillService,
        AgentTurnContext turnContext,
        SpecialistToolSet tools)
    {
        _skillService = skillService;
        _turnContext = turnContext;
        _tools = tools;

        _ownedTools.Add(tools.Add(ToolNames.ActivateSkill, ActivateSkillAsync));

        Definition = new SpecialistDefinition(Id, DisplayName, ActionBlock, _ownedTools)
        {
            // Reserved but not bound. Extraction is not a copy of the retrieval
            // one here either: only the orchestrator is given the skill manifest,
            // so it is the only agent that can be told what skills exist, which
            // makes "who activates" and "who may be told" the same question.
            ReservedAgentSlug = AgentIds.SkillActivation,
        };
    }

    /// <summary>This unit's prose and tools, as one value.</summary>
    public SpecialistDefinition Definition { get; }

    [Description(
        "Activates a skill by name to guide the conversation. Use it when the user's request matches the intent of " +
        "one of the available skills listed in the Skills section. Returns the skill's instructions to follow. " +
        "Unknown skills return an error listing the available skills.")]
    private async Task<ToolSkillActivation> ActivateSkillAsync(
        [Description("The name of the skill to activate")] string name,
        CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        _tools.Record(ToolNames.ActivateSkill, new Dictionary<string, object?>
        {
            ["name"] = name,
        });

        var skill = await _skillService.GetSkillAsync(name, cancellationToken);
        if (skill is null)
        {
            var available = _skillService.GetManifest();
            var names = available.Count == 0 ? "none" : string.Join(", ", available.Select(s => s.Name));
            var message = $"Unknown skill '{name}'. Available skills: {names}.";
            _tools.Log(ToolNames.ActivateSkill, ["name"], startedAt, 0);
            return ToolSkillActivation.NotFound(name, message);
        }

        SkillSessionState.Activate(_turnContext.State!, skill.Name, skill.Body);
        _tools.Log(ToolNames.ActivateSkill, ["name"], startedAt, 1);
        return ToolSkillActivation.Activated(skill.Name, skill.Body);
    }
}
