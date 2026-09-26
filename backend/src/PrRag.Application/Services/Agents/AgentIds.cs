namespace PrRag.Application.Services.Agents;

/// <summary>
/// The stable machine identity of every agent in the graph.
///
/// <para>
/// These are slugs, not display names. MAF documents <c>AIAgent.Id</c> as being
/// for tracking, telemetry, and distinguishing agent instances, and a handoff
/// graph resolves participants by that identity — so a slug is load-bearing in
/// two ways. A display rename must therefore never touch a value here, which is
/// why each agent's human label lives on its own
/// <c>SpecialistDefinition.DisplayName</c> instead.
/// </para>
///
/// <para>
/// They are declared as constants rather than spelled at a composition site so
/// that telemetry recorded under one slug and routing that resolves by it
/// cannot come to disagree.
/// </para>
///
/// <para>
/// Each capability unit claims the one identity reserved for it, whether or not
/// it is bound to an agent yet, through
/// <c>SpecialistDefinition.ReservedAgentSlug</c>. A constant here is therefore
/// not a declaration waiting for a use — a layer test asserts that every
/// constant below is claimed by exactly one unit and that a unit is bound only to
/// an identity it claims, so an unclaimed slug fails rather than sitting in the
/// file looking intentional.
/// </para>
/// </summary>
public static class AgentIds
{
    /// <summary>
    /// The entry point of the graph: it answers directly for the capabilities it
    /// still holds and hands off the ones extracted to their own agents.
    /// </summary>
    public const string Orchestrator = "prrag.orchestrator";

    /// <summary>
    /// Claimed by <see cref="Specialists.RequisitionSearchSpecialist"/>, which
    /// owns the read-only retrieval tools and is bound to it.
    /// </summary>
    public const string Retrieval = "prrag.retrieval";

    /// <summary>
    /// Claimed by <see cref="Specialists.RequisitionCreationSpecialist"/>, whose
    /// tools remain on the orchestrator until the write path is extracted. The
    /// slug is already fixed, so records written before and after the extraction
    /// correlate.
    /// </summary>
    public const string Creation = "prrag.creation";

    /// <summary>
    /// Claimed by <see cref="Specialists.SkillActivationSpecialist"/>, whose tool
    /// remains on the orchestrator. Not extracted alongside
    /// <see cref="Creation"/>: the orchestrator is the only agent given the skill
    /// manifest, so who may activate a skill and who may be told one exists are
    /// the same question.
    /// </summary>
    public const string SkillActivation = "prrag.skill-activation";
}
