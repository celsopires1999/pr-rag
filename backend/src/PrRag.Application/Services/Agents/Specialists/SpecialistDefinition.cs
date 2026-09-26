using Microsoft.Extensions.AI;

namespace PrRag.Application.Services.Agents.Specialists;

/// <summary>
/// One capability unit: a stable id, a human-facing name, the
/// <c>&lt;ALLOWED_ACTIONS&gt;</c> prose that documents its tools, and the tools
/// themselves.
///
/// Holding the prose and the tool list in a single value is the whole point.
/// It is the only structural link that keeps a tool's documentation attached to
/// its implementation, so Phase 2 turns each definition into one agent without
/// anyone re-deriving prose from code by hand.
/// </summary>
/// <param name="Id">
/// Stable machine identifier. Seed for the Phase 2 agent slug, which has to
/// stay distinct from the display name so a rename cannot break telemetry.
/// </param>
/// <param name="DisplayName">Human-facing label for logs and future agent names.</param>
/// <param name="ActionBlock">The action bullets for exactly the tools in <paramref name="Tools"/>.</param>
/// <param name="Tools">The tools this unit contributes to the agent.</param>
public sealed record SpecialistDefinition(
    string Id,
    string DisplayName,
    string ActionBlock,
    IList<AITool> Tools);
