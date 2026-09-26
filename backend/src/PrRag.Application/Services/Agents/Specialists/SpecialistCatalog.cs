using Microsoft.Extensions.AI;

namespace PrRag.Application.Services.Agents.Specialists;

/// <summary>
/// Resolves the three capability units and aggregates them into the single tool
/// list and action-block list the agent is built from.
///
/// The aggregation is order-sensitive and deliberately so: catalog order fixes
/// the tool order the model sees and the bullet order in the prompt, so both stay
/// stable across the refactor.
/// </summary>
public sealed class SpecialistCatalog : ISpecialistCatalog
{
    private readonly List<AITool> _allTools = [];

    public SpecialistCatalog(
        RequisitionSearchSpecialist retrieval,
        RequisitionCreationSpecialist creation,
        SkillActivationSpecialist skillActivation,
        SpecialistToolSet toolSet)
    {
        Specialists =
        [
            retrieval.Definition,
            creation.Definition,
            skillActivation.Definition,
        ];

        ActionBlocks = Specialists.Select(s => s.ActionBlock).ToList();

        foreach (var tools in Specialists.SelectMany(s => s.Tools))
        {
            _allTools.Add(tools);
        }

        // The per-unit lists are the authoritative composition, but every tool
        // registered through the shared set must be claimed by exactly one unit.
        // If a unit ever registers a tool without recording it as its own, the
        // model would silently lose it here, so fail loudly at composition time
        // rather than shipping a tool the prompt never describes.
        var registered = toolSet.All.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var claimed = _allTools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        if (!registered.SetEquals(claimed))
        {
            var unclaimed = registered.Except(claimed, StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal);
            var spurious = claimed.Except(registered, StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal);
            throw new InvalidOperationException(
                $"Capability units do not partition the registered tools. " +
                $"Registered but unclaimed: [{string.Join(", ", unclaimed)}]. " +
                $"Claimed but not registered: [{string.Join(", ", spurious)}].");
        }
    }

    public IReadOnlyList<SpecialistDefinition> Specialists { get; }

    public IList<AITool> AllTools => _allTools;

    public IReadOnlyList<string> ActionBlocks { get; }
}
