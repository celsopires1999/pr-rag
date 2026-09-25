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
    /// <summary>The session the current turn is running against.</summary>
    public AgentSession? Session { get; set; }

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
    /// Set when a <c>create_requisition</c> call actually persisted a requisition.
    /// A latch rather than a read of the session draft state, because a
    /// successful creation clears the draft and the report still has to show
    /// that a confirmation preceded it.
    /// </summary>
    public bool RequisitionPersisted { get; set; }

    public void Begin(AgentSession session, string sessionId, int topK, double minSimilarity)
    {
        Session = session;
        SessionId = sessionId;
        TopK = topK;
        MinSimilarity = minSimilarity;
        RewrittenQuery = null;
        RetrievedItems.Clear();
        ToolCalls.Clear();
        DraftStaged = false;
        DraftPresented = false;
        DraftConfirmed = false;
        RequisitionPersisted = false;
    }
}