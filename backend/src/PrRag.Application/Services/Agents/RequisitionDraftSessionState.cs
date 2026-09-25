using System.Text.Json;
using Microsoft.Agents.AI;
using PrRag.Application.Domain;

namespace PrRag.Application.Services.Agents;

/// <summary>
/// Owns the requisition-draft keys and logic of the <see cref="AgentSession"/>
/// state bag: staging a draft, marking it presented, recording the user's
/// confirmation, and clearing once a requisition is persisted. Mirrors
/// <see cref="SkillSessionState"/> — the draft is deliberately session-scoped,
/// not a database row, because an unconfirmed proposal is worthless once the
/// conversation ends.
///
/// The draft is stored as a JSON string for the same reason the skill body is
/// stored as a plain string: the state bag's persistence is not something the
/// tool handlers should depend on knowing the CLR type of.
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
    private const string DraftKey = "RequisitionDraft";
    private const string DraftPresentedKey = "RequisitionDraftPresented";
    private const string DraftConfirmedKey = "RequisitionDraftConfirmed";

    private const string True = "true";

    /// <summary>
    /// Stores a draft and marks it presented. Any prior draft is replaced and
    /// the confirmed flag is cleared, so a re-draft always requires a fresh
    /// presentation and confirmation.
    /// </summary>
    public static void Stage(AgentSession session, RequisitionDraft draft)
    {
        session.StateBag.SetValue(DraftKey, JsonSerializer.Serialize(draft));
        session.StateBag.SetValue(DraftPresentedKey, True);
        session.StateBag.TryRemoveValue(DraftConfirmedKey);
    }

    /// <summary>Returns the session's draft and its progress flags.</summary>
    public static RequisitionDraftSnapshot Read(AgentSession? session)
    {
        if (session is null)
        {
            return default;
        }

        var presented = ReadFlag(session, DraftPresentedKey);
        var confirmed = ReadFlag(session, DraftConfirmedKey);

        if (!session.StateBag.TryGetValue<string>(DraftKey, out var json) || string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        RequisitionDraft? draft;
        try
        {
            draft = JsonSerializer.Deserialize<RequisitionDraft>(json);
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

        return new RequisitionDraftSnapshot(draft, presented, confirmed);
    }

    /// <summary>
    /// Marks a presented draft as confirmed. Returns false when there is no
    /// draft or it has not been presented, leaving the state unchanged.
    /// </summary>
    public static bool MarkConfirmed(AgentSession session)
    {
        var current = Read(session);
        if (current.Draft is null || !current.Presented)
        {
            return false;
        }

        session.StateBag.SetValue(DraftConfirmedKey, True);
        return true;
    }

    /// <summary>Drops all requisition-draft state from the session state bag.</summary>
    public static void Clear(AgentSession session)
    {
        session.StateBag.TryRemoveValue(DraftKey);
        session.StateBag.TryRemoveValue(DraftPresentedKey);
        session.StateBag.TryRemoveValue(DraftConfirmedKey);
    }

    private static bool ReadFlag(AgentSession session, string key) =>
        session.StateBag.TryGetValue<string>(key, out var value) && value == True;
}
