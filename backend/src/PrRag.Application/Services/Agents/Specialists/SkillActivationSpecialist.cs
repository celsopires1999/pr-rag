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

        Definition = new SpecialistDefinition(Id, DisplayName, ActionBlock, _ownedTools);
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

        SkillSessionState.Activate(_turnContext.Session!, skill.Name, skill.Body);
        _tools.Log(ToolNames.ActivateSkill, ["name"], startedAt, 1);
        return ToolSkillActivation.Activated(skill.Name, skill.Body);
    }
}
