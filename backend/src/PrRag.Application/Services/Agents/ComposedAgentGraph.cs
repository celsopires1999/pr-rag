using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace PrRag.Application.Services.Agents;

/// <summary>
/// The composed graph: the single agent callers run, plus the participants and
/// the workflow behind it.
///
/// <para>
/// Callers use <see cref="Agent"/>. The rest is here so the per-agent
/// assignments can be asserted and so routing can be reached without
/// re-deriving it; nothing outside this namespace should need it.
/// </para>
/// </summary>
/// <param name="Agent">The graph presented as one agent.</param>
/// <param name="Orchestrator">The graph's entry point.</param>
/// <param name="Participants">Specialist agents by their stable slug.</param>
/// <param name="Workflow">The underlying graph.</param>
public sealed record ComposedAgentGraph(
    AIAgent Agent,
    ChatClientAgent Orchestrator,
    IReadOnlyDictionary<string, ChatClientAgent> Participants,
    Workflow Workflow);
