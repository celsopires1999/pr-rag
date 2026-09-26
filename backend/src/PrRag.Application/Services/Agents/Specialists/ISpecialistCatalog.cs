using Microsoft.Extensions.AI;

namespace PrRag.Application.Services.Agents.Specialists;

/// <summary>
/// The set of capability units the purchase-requisition agent is composed from,
/// plus the combined tool list and action blocks that composition needs.
///
/// Phase 1 resolves this to the three units and flattens them, so the model sees
/// exactly the tools and prompt it saw before the split. Phase 2 resolves the
/// same catalog into a workflow of real agents instead of flattening it, which
/// is why the units and their definitions — not a flattened tool list — are what
/// this exposes.
/// </summary>
public interface ISpecialistCatalog
{
    /// <summary>The capability units, each with its prose and its own tools.</summary>
    IReadOnlyList<SpecialistDefinition> Specialists { get; }

    /// <summary>Every tool across every unit, in catalog order.</summary>
    IList<AITool> AllTools { get; }

    /// <summary>
    /// Every unit's action block, in catalog order, ready to be concatenated
    /// into the single <c>&lt;ALLOWED_ACTIONS&gt;</c> section of the prompt.
    /// </summary>
    IReadOnlyList<string> ActionBlocks { get; }
}
