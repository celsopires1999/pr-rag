using PrRag.Application.Domain;

namespace PrRag.Application.DTOs;

public readonly record struct RequisitionSearchResult(
    Domain.PurchaseRequisition Requisition,
    double? Similarity);

public sealed class RagQueryReport
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public string Question { get; set; } = string.Empty;

    public int TopK { get; set; }

    public double MinSimilarity { get; set; }

    public bool TopKFromRequest { get; set; }

    public bool MinSimilarityFromRequest { get; set; }

    public string? RewrittenQuery { get; set; }

    public string? SkillId { get; set; }

    public string? SkillName { get; set; }

    public bool SkillActivated { get; set; }

    /// <summary>Whether a requisition draft was staged during the turn.</summary>
    /// <remarks>
    /// An outcome, not an attempt: staging is refused outright for a field set that
    /// does not validate, and a turn that called <c>create_requisition_draft</c> and
    /// got a rejection staged nothing. The attempt is
    /// <see cref="WriteAttempted"/>.
    /// </remarks>
    public bool RequisitionDraftStaged { get; set; }

    /// <summary>Whether a requisition draft is currently presented to the user.</summary>
    public bool RequisitionDraftPresented { get; set; }

    /// <summary>Whether a requisition draft was confirmed by the user.</summary>
    /// <remarks>
    /// An outcome for the same reason as <see cref="RequisitionDraftStaged"/>: a
    /// declined or premature <c>confirm_requisition_draft</c> call is an attempt that
    /// recorded nothing.
    /// </remarks>
    public bool RequisitionDraftConfirmed { get; set; }

    /// <summary>
    /// Whether a <c>create_requisition</c> call in this turn actually persisted a
    /// requisition. False when the call was refused — most importantly when it
    /// was refused for want of a confirmed draft.
    /// </summary>
    public bool RequisitionPersisted { get; set; }

    public List<RagRetrievedItem> RetrievedItems { get; set; } = new();

    public List<RagToolCall> ToolCalls { get; set; } = new();

    public string Answer { get; set; } = string.Empty;

    public int RetrievedCount { get; set; }

    public bool UsedNoContextFallback { get; set; }

    /// <summary>
    /// The capability slug the turn entered at: the orchestrator, or the creation
    /// capability when a draft was awaiting confirmation.
    /// </summary>
    /// <remarks>
    /// The entry point moved from being always-the-orchestrator to being a
    /// function of session state, and the report was the wrong shape for that: an
    /// operator reading a bad answer could not tell which agent produced it. The
    /// entry slug is deterministic application state, so unlike the answer it
    /// cannot be wrong, which makes it the first thing to check when a turn goes
    /// sideways.
    /// </remarks>
    public string? EntryAgent { get; set; }

    /// <summary>
    /// The handoffs the turn made, in call order. Empty means no agent handed the
    /// turn on.
    /// </summary>
    /// <remarks>
    /// A handoff is a workflow edge, not an application tool, so it contributes no
    /// entry to <see cref="ToolCalls"/>. That made two opposite failures look
    /// identical in this report: an entry agent that answered a request it should
    /// have routed, and one that routed correctly to a specialist which then gave a
    /// bad answer. They are distinguishable only by the pair of slugs recorded
    /// here, which is why the same reasoning that added
    /// <see cref="RetrievalAttempted"/> and <see cref="WriteAttempted"/> applies to
    /// routing.
    /// </remarks>
    public List<RagHandoff> Handoffs { get; set; } = new();

    /// <summary>
    /// The capability slug of the agent that authored the turn's answer: the last
    /// agent to emit non-empty text. Null means no agent produced text at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="EntryAgent"/> and <see cref="Handoffs"/> between them describe a
    /// <em>chain</em>, and a chain does not say who wrote the sentence the caller
    /// received. So "the entry agent answered a request it should have routed" and
    /// "the entry agent routed correctly and the specialist then answered badly"
    /// were the same report — the pair of opposite failures that cost two changes'
    /// worth of work before the shipped observability refuted a story built on it.
    /// </para>
    /// <para>
    /// "Last to emit text" is the definition of authorship rather than an
    /// approximation of it: the run's answer <em>is</em> the last text any agent
    /// emitted, so text that was produced and then discarded was not the answer
    /// either. The alternative — the furthest handoff target — is stable when the
    /// entry agent resumes and speaks, and wrong whenever the target does not
    /// produce the final text.
    /// </para>
    /// <para>
    /// Null is a real value and is never defaulted to <see cref="EntryAgent"/>. A
    /// run whose chat client threw completes empty and <c>/api/chat</c> answers 502,
    /// and that turn's report is written before the throw so it stays diagnosable.
    /// A defaulted author would make it read as an ordinary turn by the entry agent,
    /// which is the one thing this field exists to distinguish — so the field's only
    /// failure signal would be the one thing a default erases.
    /// </para>
    /// </remarks>
    public string? AnswerAgent { get; set; }

    /// <summary>
    /// Whether a read-only tool actually ran this turn.
    /// </summary>
    /// <remarks>
    /// This exists because <see cref="UsedNoContextFallback"/> cannot answer the
    /// question an operator asks when a turn returns nothing: "did we search and
    /// find nothing, or did the model never search?" Both are
    /// <c>RetrievedCount == 0</c> and both set the fallback flag, so the two cases
    /// were indistinguishable in the report.
    /// <para>
    /// The live gate hit exactly the second case. A turn narrated "Observation:
    /// Executed the search" and returned nothing, with no tool call in the report
    /// — the model claimed a search that never ran. With this field the claim is
    /// at least checkable: <c>RetrievalAttempted == false</c> alongside an answer
    /// that describes results is a fabricated retrieval.
    /// </para>
    /// </remarks>
    public bool RetrievalAttempted { get; set; }

    /// <summary>
    /// Whether a tool on the write path actually ran this turn.
    /// </summary>
    /// <remarks>
    /// The write-side mirror of <see cref="RetrievalAttempted"/>, and it exists for
    /// the same reason. Every other write fact in this report is an outcome:
    /// <see cref="RequisitionDraftStaged"/>,
    /// <see cref="RequisitionDraftConfirmed"/>, and
    /// <see cref="RequisitionPersisted"/> are all false both when a write was
    /// refused and when no write was attempted. A model that calls
    /// <c>create_requisition</c> with no confirmed draft — the case the gate exists
    /// to hold — is therefore indistinguishable in this report from a turn that
    /// never tried, and so is a turn whose answer claims a requisition was created.
    /// <para>
    /// With this flag the claim is checkable without querying the table: an answer
    /// asserting a creation alongside <c>WriteAttempted == false</c> is a fabricated
    /// completion, which is the write-path analogue of the "Observation: Executed the
    /// search" defect that <see cref="RetrievalAttempted"/> was added for. It is
    /// derived from the tool call list rather than from the session's draft state,
    /// because a refused write leaves the draft state exactly as it was — the same
    /// reading would then report a turn that tried and was stopped identically to
    /// one that never spoke to the write path.
    /// </para>
    /// </remarks>
    public bool WriteAttempted { get; set; }
}

public sealed class RagRetrievedItem
{
    public string PurchaseRequisitionId { get; set; } = string.Empty;

    public string SupplierCode { get; set; } = string.Empty;

    public string SupplierName { get; set; } = string.Empty;

    public string Item { get; set; } = string.Empty;

    public string ItemName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public double? Similarity { get; set; }

    public static RagRetrievedItem From(PurchaseRequisition r, double? similarity) => new()
    {
        PurchaseRequisitionId = r.PurchaseRequisitionId,
        SupplierCode = r.SupplierCode,
        SupplierName = r.SupplierName,
        Item = r.Item,
        ItemName = r.ItemName,
        Description = r.Description,
        Similarity = similarity,
    };
}

public sealed class RagToolCall
{
    public string Name { get; set; } = string.Empty;

    public IReadOnlyDictionary<string, object?> Arguments { get; set; } =
        new Dictionary<string, object?>();
}

/// <summary>One capability handing a turn to another.</summary>
public sealed class RagHandoff
{
    /// <summary>The capability slug that handed the turn on.</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>The capability slug that received the turn.</summary>
    public string To { get; set; } = string.Empty;
}
