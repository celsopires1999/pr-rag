using Microsoft.Extensions.AI;

namespace PrRag.Application.Services.Agents;

/// <summary>
/// Everything that must outlive a single turn for one client session.
///
/// <para>
/// This used to live in <c>AgentSession.StateBag</c>, which coupled it to the
/// lifetime of the MAF session object. That coupling is what forced the agent
/// graph to be as long-lived as the session, and the graph cannot be: its tools
/// capture a scoped repository and a per-turn context, so a session that outlived
/// a request left the retrieval tools writing into a disposed scope and froze the
/// skill manifest at whatever the first turn saw. Sessions are now created per
/// turn and this record is what carries the conversation across them.
/// </para>
///
/// <para>
/// Types are stored as primitives on purpose (see
/// <see cref="RequisitionDraftSessionState"/>): the tool handlers should not have
/// to know how the framework happens to persist a value.
/// </para>
/// </summary>
public sealed class AgentSessionState
{
    /// <summary>
    /// The conversation so far, oldest first. Owned by <c>ChatService</c>: the
    /// agents' own history providers are inert, so this is the only history.
    /// </summary>
    public List<ChatMessage> History { get; } = [];

    /// <summary>Name of the skill activated for this session, if any.</summary>
    public string? SkillId { get; set; }

    /// <summary>Guidance body of the active skill, injected on the next turn only.</summary>
    public string? SkillBody { get; set; }

    /// <summary>Set once the active skill's guidance has been injected as a message.</summary>
    public bool SkillBodyInjected { get; set; }

    /// <summary>The staged requisition draft, serialized.</summary>
    public string? RequisitionDraftJson { get; set; }

    /// <summary>Set when the staged draft has been shown to the user.</summary>
    public bool RequisitionDraftPresented { get; set; }

    /// <summary>Set when the user confirmed the presented draft.</summary>
    public bool RequisitionDraftConfirmed { get; set; }
}
