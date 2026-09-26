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
/// <param name="DisplayName">Human-facing label for logs and for the agent's name.</param>
/// <param name="ActionBlock">The action bullets for exactly the tools in <paramref name="Tools"/>.</param>
/// <param name="Tools">The tools this unit contributes to its agent.</param>
public sealed record SpecialistDefinition(
    string Id,
    string DisplayName,
    string ActionBlock,
    IList<AITool> Tools)
{
    /// <summary>
    /// The agent slug this capability runs as, or <c>null</c> when it is not yet
    /// bound to its own agent and its tools therefore stay on the orchestrator.
    ///
    /// <para>
    /// A non-null value is what makes a unit an agent: it is the switch the
    /// composition reads to decide between "give this unit its own agent" and
    /// "leave these tools on the orchestrator". The unit declares it rather than
    /// the composer, so the answer to "is this capability extracted?" lives with
    /// the capability instead of in a routing list somewhere else.
    /// </para>
    ///
    /// <para>
    /// This is the stable identity; <see cref="DisplayName"/> is presentation.
    /// The two must never be merged, because handoff resolves participants by
    /// identity and a rename would then change routing.
    /// </para>
    /// </summary>
    public string? AgentSlug { get; init; }

    /// <summary>
    /// The agent identity reserved for this capability, whether or not it is
    /// bound yet.
    /// </summary>
    ///
    /// <para>
    /// <see cref="AgentSlug"/> alone cannot express "this capability will get
    /// its own agent, and it does not have one yet": a null slug is
    /// indistinguishable from a capability nobody ever intends to extract. That
    /// ambiguity is why the reserved slugs used to live only in prose on
    /// <see cref="AgentIds"/> — a doc comment that no test could check, so an
    /// identity reserved for the write path was indistinguishable from one
    /// invented by accident.
    /// </para>
    ///
    /// <para>
    /// Declaring the reservation here makes the pairing checkable: a unit may be
    /// bound only to an identity it reserves, every reserved identity belongs to
    /// exactly one unit, and every <see cref="AgentIds"/> constant is claimed by
    /// some unit. Extracting a capability then changes
    /// <see cref="IsExtracted"/> and nothing else — the identity was already
    /// fixed, so telemetry recorded before and after the extraction share a
    /// slug.
    /// </para>
    /// </summary>
    public required string ReservedAgentSlug { get; init; }

    /// <summary>Whether this capability has been extracted to its own agent.</summary>
    public bool IsExtracted => AgentSlug is not null;
}
