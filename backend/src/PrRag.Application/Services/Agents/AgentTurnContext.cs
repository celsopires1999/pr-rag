using Microsoft.Agents.AI;
using PrRag.Application.DTOs;

namespace PrRag.Application.Services.Agents;

/// <summary>
/// Scoped, per-turn state shared between the chat orchestrator and the agent
/// tools for a single run: the active session plus the retrieval
/// parameters and bookkeeping that the tools read/write each turn. Transient by
/// design — never persisted in the session state bag (unlike skill state).
/// </summary>
public sealed class AgentTurnContext
{
    /// <summary>The per-turn MAF session. Fresh every turn — see <see cref="AgentSessionState"/>.</summary>
    public AgentSession? Session { get; set; }

    /// <summary>
    /// The conversation state that outlives the turn. The tools reach the active
    /// skill and the staged draft through this rather than the session, because the
    /// session is discarded when the turn ends and those must not be.
    /// </summary>
    public AgentSessionState? State { get; set; }

    /// <summary>The client-facing session id the turn belongs to.</summary>
    public string? SessionId { get; set; }

    public int TopK { get; set; }

    public double MinSimilarity { get; set; }

    /// <summary>Set by the semantic search tool when the model calls it.</summary>
    public string? RewrittenQuery { get; set; }

    public List<RagRetrievedItem> RetrievedItems { get; } = new();

    /// <summary>Tool invocations recorded by the tool handlers during the turn, in call order.</summary>
    public List<RagToolCall> ToolCalls { get; } = new();

    /// <summary>Set when a requisition draft was staged during this turn.</summary>
    public bool DraftStaged { get; set; }

    /// <summary>Set when a requisition draft was presented for confirmation during this turn.</summary>
    public bool DraftPresented { get; set; }

    /// <summary>Set when the user confirmed a requisition draft during this turn.</summary>
    public bool DraftConfirmed { get; set; }

    /// <summary>
    /// The capability slug the turn entered at, from
    /// <see cref="AgentGraphComposer.ResolveEntryPoint"/>. It is deterministic
    /// application state rather than a model decision, and it is what the report
    /// needs to say which agent was on the hook for this turn.
    /// </summary>
    public string? EntryAgent { get; set; }

    /// <summary>
    /// Whether the session held a staged, unwritten requisition draft at the
    /// moment the entry point was resolved. Read from the same
    /// <see cref="RequisitionDraftSessionState.HasUnwrittenDraft"/> predicate that
    /// chose <see cref="EntryAgent"/>, and recorded beside it so the two cannot
    /// disagree.
    /// </summary>
    /// <remarks>
    /// Nullable rather than a bool, for the same reason <see cref="EntryAgent"/>
    /// is: a turn that never composed has read no state, so null is "not asked"
    /// and false is "asked, and no draft" — a distinction a report built from a
    /// reused context would otherwise blur.
    /// </remarks>
    public bool? RequisitionDraftPendingAtEntry { get; set; }

    /// <summary>
    /// The handoffs the turn made, in call order and deduplicated. Empty is
    /// meaningful: it means no agent handed the turn on, which no other report
    /// field can distinguish from a handoff that happened.
    /// </summary>
    public List<RagHandoff> Handoffs { get; } = new();

    /// <summary>
    /// The capability slug that authored this turn's answer — the last agent to
    /// emit non-empty text, written once per agent that has something to say.
    /// Null means no agent produced text.
    /// </summary>
    /// <remarks>
    /// Last write wins, deliberately, and with no coordination between the writing
    /// parties: every agent's decorator writes this one field on this shared
    /// object, so the run's own ordering decides the value. A recorded author is
    /// therefore exactly the last agent that spoke, which is the same thing the
    /// answer is — see <see cref="RagQueryReport.AnswerAgent"/> for why that is
    /// the definition rather than an approximation of it.
    /// </remarks>
    public string? AnswerAgent { get; private set; }

    /// <summary>
    /// Records a handoff once. Deduplicated because a streamed call arrives as one
    /// update carrying the name and further updates carrying argument deltas, and
    /// counting those separately would report a fraction of a handoff as several.
    /// </summary>
    public void RecordHandoff(string from, string to)
    {
        if (!Handoffs.Any(h => h.From == from && h.To == to))
        {
            Handoffs.Add(new RagHandoff { From = from, To = to });
        }
    }

    /// <summary>
    /// Attributes the turn's answer to <paramref name="slug"/>, overwriting any
    /// earlier attribution.
    /// </summary>
    /// <remarks>
    /// A private setter with this method rather than a public property, because the
    /// only correct way to set the field is with an agent that actually spoke. There
    /// is no "no agent spoke" value to pass: a caller that has no author leaves the
    /// field at null, which is the fact the report is meant to carry.
    /// </remarks>
    public void RecordAnswerAuthor(string slug) => AnswerAgent = slug;

    /// <summary>
    /// Set when a <c>create_requisition</c> call actually persisted a requisition.
    /// A latch rather than a read of the session draft state, because a
    /// successful creation clears the draft and the report still has to show
    /// that a confirmation preceded it.
    /// </summary>
    public bool RequisitionPersisted { get; set; }

    /// <summary>
    /// Establishes the turn's context. Called before the run is composed,
    /// because the entry point is chosen from <see cref="State"/> — see
    /// <see cref="AgentGraphComposer.ResolveEntryPoint"/>. The MAF session is
    /// attached afterwards by <see cref="AttachSession"/>, because the graph
    /// that owns it is not composed until the state is known.
    /// </summary>
    public void Begin(AgentSessionState state, string sessionId, int topK, double minSimilarity)
    {
        Session = null;
        State = state;
        SessionId = sessionId;
        TopK = topK;
        MinSimilarity = minSimilarity;
        RewrittenQuery = null;
        RetrievedItems.Clear();
        ToolCalls.Clear();
        Handoffs.Clear();
        // Same reason as the list above, and the same hazard: a turn that inherits
        // its predecessor's author would report an unrouted answer as a delegated
        // one, which is the specific misreading this field exists to prevent.
        AnswerAgent = null;
        // Null, not false, because composition has not happened yet: this is
        // reset before the entry point is resolved, so a value here would be the
        // previous turn's reading of the session. Whatever the graph finds is
        // written after, and a turn that never composes leaves it null.
        RequisitionDraftPendingAtEntry = null;
        DraftStaged = false;
        DraftPresented = false;
        DraftConfirmed = false;
        RequisitionPersisted = false;
    }

    public void AttachSession(AgentSession session) => Session = session;
}