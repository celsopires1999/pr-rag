using System.Text.Json;
using PrRag.Application.Domain;

namespace PrRag.Application.Services.Agents;

/// <summary>
/// Owns the requisition-draft state of a <see cref="AgentSessionState"/>:
/// staging a draft, marking it presented, recording the user's confirmation, and
/// clearing once a requisition is persisted. Mirrors
/// <see cref="SkillSessionState"/> — the draft is deliberately session-scoped,
/// not a database row, because an unconfirmed proposal is worthless once the
/// conversation ends.
///
/// The draft is stored as a JSON string for the same reason the skill body is
/// stored as a plain string: the tool handlers should not depend on knowing the
/// CLR type the state happens to be held as.
///
/// Staging marks the draft presented in one step, because
/// <c>create_requisition_draft</c> returns the values specifically so the model
/// can present them; there is no state where a draft is staged but the model
/// has not been handed it. The observability report reads per-turn latches on
/// <see cref="AgentTurnContext"/> rather than this state, because a successful
/// creation clears the draft and the report still has to show the confirmation
/// that preceded it.
/// </summary>
public static class RequisitionDraftSessionState
{
    /// <summary>
    /// Stores a draft and marks it presented. Any prior draft is replaced and
    /// the confirmed flag is cleared, so a re-draft always requires a fresh
    /// presentation and confirmation.
    /// </summary>
    public static void Stage(AgentSessionState state, RequisitionDraft draft)
    {
        state.RequisitionDraftJson = JsonSerializer.Serialize(draft);
        state.RequisitionDraftPresented = true;
        state.RequisitionDraftConfirmed = false;
    }

    /// <summary>Returns the session's draft and its progress flags.</summary>
    public static RequisitionDraftSnapshot Read(AgentSessionState? state)
    {
        if (state is null || string.IsNullOrWhiteSpace(state.RequisitionDraftJson))
        {
            return default;
        }

        RequisitionDraft? draft;
        try
        {
            draft = JsonSerializer.Deserialize<RequisitionDraft>(state.RequisitionDraftJson);
        }
        catch (JsonException)
        {
            // A draft we cannot read back is treated as absent; the gate then
            // refuses creation, which is the safe direction.
            return default;
        }

        if (draft is null)
        {
            return default;
        }

        return new RequisitionDraftSnapshot(draft, state.RequisitionDraftPresented, state.RequisitionDraftConfirmed);
    }

    /// <summary>
    /// Marks a presented draft as confirmed. Returns false when there is no
    /// draft or it has not been presented, leaving the state unchanged.
    /// </summary>
    public static bool MarkConfirmed(AgentSessionState state)
    {
        var current = Read(state);
        if (current.Draft is null || !current.Presented)
        {
            return false;
        }

        state.RequisitionDraftConfirmed = true;
        return true;
    }

    /// <summary>Drops all requisition-draft state.</summary>
    public static void Clear(AgentSessionState state)
    {
        state.RequisitionDraftJson = null;
        state.RequisitionDraftPresented = false;
        state.RequisitionDraftConfirmed = false;
    }
}
