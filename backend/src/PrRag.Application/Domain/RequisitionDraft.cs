namespace PrRag.Application.Domain;

/// <summary>
/// An unpersisted purchase requisition proposed during a session. Holds only
/// the six persisted field values; how far the draft has progressed through the
/// draft/present/confirm flow lives in
/// <see cref="RequisitionDraftSnapshot"/> so the flags can change without
/// rewriting the field values.
/// </summary>
public sealed record RequisitionDraft(
    string SupplierCode,
    string Item,
    string Description,
    decimal Quantity,
    string Date,
    string Requester);

/// <summary>
/// A session's requisition draft together with its progress flags. Draft is
/// null when nothing is staged.
/// </summary>
public readonly record struct RequisitionDraftSnapshot(RequisitionDraft? Draft, bool Presented, bool Confirmed)
{
    /// <summary>True only when a draft exists and the user confirmed it.</summary>
    public bool HasConfirmedDraft => Draft is not null && Confirmed;
}
