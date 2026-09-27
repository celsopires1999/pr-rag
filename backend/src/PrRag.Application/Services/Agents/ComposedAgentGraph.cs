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
/// <param name="EntryAgent">
/// The agent the turn started at. Not always the orchestrator — see
/// <see cref="AgentGraphComposer.ResolveEntryPoint"/>.
/// </param>
/// <param name="EntryPoint">
/// The stable slug of <paramref name="EntryAgent"/>. Kept beside the agent
/// because the whole point is that the entry varies per turn, and an assertion
/// that reads the agent's display name cannot see the difference.
/// </param>
/// <param name="Specialists">Every specialist agent by its stable slug, entry included.</param>
/// <param name="Workflow">The underlying graph.</param>
public sealed record ComposedAgentGraph(
    AIAgent Agent,
    ChatClientAgent EntryAgent,
    string EntryPoint,
    IReadOnlyDictionary<string, ChatClientAgent> Specialists,
    Workflow Workflow);
