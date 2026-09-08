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

    public void Begin(AgentSession session, string sessionId, int topK, double minSimilarity)
    {
        Session = session;
        SessionId = sessionId;
        TopK = topK;
        MinSimilarity = minSimilarity;
        RewrittenQuery = null;
        RetrievedItems.Clear();
    }
}