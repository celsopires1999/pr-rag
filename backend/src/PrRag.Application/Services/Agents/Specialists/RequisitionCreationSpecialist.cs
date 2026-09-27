using System.ComponentModel;
using Microsoft.Extensions.AI;
using System.Diagnostics;
using PrRag.Application.Abstractions;
using PrRag.Application.Domain;
using PrRag.Application.DTOs;

namespace PrRag.Application.Services.Agents.Specialists;

/// <summary>
/// Creation capability: the three tools that stage, confirm, and persist a
/// requisition (<c>create_requisition_draft</c>, <c>confirm_requisition_draft</c>,
/// <c>create_requisition</c>).
///
/// The confirmation gate lives in <see cref="CreateRequisitionAsync"/>, in code,
/// not in prompt text: the gate is advisory if it is only prose, and the model
/// demonstrably skips advisory steps. The prose in <see cref="ActionBlock"/>
/// restates the gate for the model, but the refusal below is what actually
/// enforces it. That is why the gate text and the gate implementation live in
/// the same unit.
/// </summary>
public sealed class RequisitionCreationSpecialist
{
    public const string Id = "purchase-requisition-creation-specialist";

    public const string DisplayName = "Purchase-requisition creation";

    /// <summary>
    /// The action bullets for this unit's tools plus the creation-gate rules,
    /// moved verbatim out of <see cref="AgentInstructions.CoreInstructions"/>. The
    /// gate prose sits here rather than in the core prompt so that the rule the
    /// model is asked to follow and the code that refuses when it is skipped are
    /// one unit apart at most.
    ///
    /// <para>
    /// It names no retrieval tool, deliberately. This unit is not offered the read
    /// tools, so a wire name here would be an instruction the agent cannot follow
    /// and nothing would fail — the exact failure the shipped create skill had,
    /// where a step silently never ran. The rule is stated without the names:
    /// lookups belong to another capability, so this one hands the question off.
    /// <c>AgentFrameworkLayeringTests</c> asserts the absence rather than trusting
    /// it.
    /// </para>
    /// <para>
    /// The closing paragraph separates two turns that read alike — one asking to
    /// skip the confirmation, one answering a confirmation — because conflating
    /// them cost roughly half the turns of the adversarial probe. Both name the
    /// confirmation step, both arrive at an agent holding no draft, and the rule
    /// for the second shape ("you hold no draft, so say none is awaiting
    /// confirmation") read as though it answered the first, which is a new request
    /// that needs a draft staged. The turn carried every field and this agent
    /// answered that it was waiting on a confirmation nobody had made. It is
    /// recorded here as a rate because nothing is written and nothing is claimed
    /// false on two of the three shapes; the third — asserting a draft exists that
    /// was never staged — is a fabricated fact, and the prompt now names it
    /// outright.
    /// </para>
    /// </summary>
    public const string ActionBlock =
        """
        * **`create_requisition_draft`**: Stage the six requisition fields once you have collected them from the user. Writes nothing to the database.
        * **`confirm_requisition_draft`**: Record the user's explicit yes after you have presented the staged draft and asked for confirmation.
        * **`create_requisition`**: Persist the requisition. It is REFUSED unless a draft the user confirmed exists, so it is your LAST step, never your first.
            * *Required parameters (must be extracted from user input):* `supplierCode`, `item`, `description`, `quantity` (positive number), `date` (ISO format yyyy-MM-dd), `requester`.

        Never call `create_requisition` directly, and never treat the user's listing of the fields as their
        confirmation of the draft. A field list is not consent: the user must respond to the draft you presented.

        A turn reaches you directly whenever a draft is staged and not yet written. That means no skill was
        activated for the turn: skill activation belongs to the agent that fronts the conversation, and it is not on
        this path. You have no tool that reads the active skill and none that changes it, so do not ask the user to
        activate one and do not ask them to start over. Answer from the draft you already hold and keep working the
        gate.

        You own no lookup tool. Searching requisitions and resolving an item name or a supplier code belong to
        another capability, so if the user asks you to look something up, or to check whether an item and supplier
        go together, hand that question off rather than answering it. Never assert a code is valid, and never let a
        lookup stand in for the confirmation gate: `create_requisition` runs its own check and refuses an
        unregistered combination.

        You are the only agent that knows whether a draft is actually waiting, so state that plainly rather than
        guessing at what the user meant. Establish it from what this turn gives you, not from what its wording
        implies.

        If this turn carries the requisition's fields — a complete request, or one asking you to skip the
        confirmation — then no draft is waiting yet, and that is the ordinary case: stage one now with
        `create_requisition_draft`. A complete request is what stages a draft, so the completeness of the details is
        the reason to call that tool, never a reason to believe one already exists. "Skip the confirmation", "do not
        ask me to confirm", and "I already gave you the details" are the user stating a preference about the step
        that comes after staging. They are not claims that a draft is awaiting them. Stage, present, and ask for the
        confirmation as normal: an unconfirmed draft is the only thing you can leave behind, and writing one is the
        only irreversible thing you can do.

        Only one shape is a question about draft state: the user is answering a draft you actually presented — "yes, I
        confirm it", "go ahead and create it" — and you hold none. Then, and only then, say that no requisition draft
        is awaiting their confirmation and ask them to describe the requisition. The test is whether the user is
        answering a draft you presented, not whether their turn happens to contain the word "confirmation": a turn
        that also carries the six fields is a new request, not a confirmation of nothing.

        Never assert a draft you have not staged, and never present one you have not staged. A claim that a draft
        exists is a fabricated fact, and it is the one failure on this path that is not safe. Never tell the user you
        lack information or details on a creation request when the answer is instead that you hold no draft, or that
        you are waiting on their confirmation. That reason is checkable and yours to give; "I don't have enough
        information" is neither, and it sends the user off to supply something they already supplied.
        """;

    private static readonly string[] CreateRequisitionArguments =
        ["supplierCode", "item", "description", "quantity", "date", "requester"];

    private readonly IRequisitionWriter _requisitionWriter;
    private readonly IPurchaseRequisitionRepository _repository;
    private readonly AgentTurnContext _turnContext;
    private readonly SpecialistToolSet _tools;
    private readonly List<AITool> _ownedTools = [];

    public RequisitionCreationSpecialist(
        IRequisitionWriter requisitionWriter,
        IPurchaseRequisitionRepository repository,
        AgentTurnContext turnContext,
        SpecialistToolSet tools)
    {
        _requisitionWriter = requisitionWriter;
        _repository = repository;
        _turnContext = turnContext;
        _tools = tools;

        _ownedTools.Add(tools.Add(ToolNames.CreateRequisitionDraft, CreateRequisitionDraftAsync));
        _ownedTools.Add(tools.Add(ToolNames.ConfirmRequisitionDraft, ConfirmRequisitionDraftAsync));
        _ownedTools.Add(tools.Add(ToolNames.CreateRequisition, CreateRequisitionAsync));

        Definition = new SpecialistDefinition(Id, DisplayName, ActionBlock, _ownedTools)
        {
            // Bound, unlike skill activation: the write path is the only capability
            // whose tools are irreversible, and the orchestrator is the agent whose
            // job is to answer directly and route, so it is the one least
            // constrained by a single job. Extraction also gives this capability a
            // deterministic entry point on a confirmation turn — see
            // AgentGraphComposer.ResolveEntryPoint.
            ReservedAgentSlug = AgentIds.Creation,
            AgentSlug = AgentIds.Creation,
        };
    }

    /// <summary>This unit's prose and tools, as one value.</summary>
    public SpecialistDefinition Definition { get; }

    [Description(
        "Stages a new purchase requisition as an unconfirmed draft in this session. Nothing is written to the " +
        "database by this tool. Call it once you have collected all six fields from the user. It then returns the " +
        "draft for you to present back to the user as a summary and ask for explicit confirmation. " +
        "Re-calling it replaces the previous draft and discards any earlier confirmation. " +
        "Required parameters: supplierCode, item, description, quantity (a positive number), date (ISO format yyyy-MM-dd), requester.")]
    private Task<ToolDraftStaged> CreateRequisitionDraftAsync(
        [Description("The code of the supplier")] string supplierCode,
        [Description("The code of the item")] string item,
        [Description("The description of the requisition")] string description,
        [Description("The quantity of the item")] decimal quantity,
        [Description("The date of the requisition")] string date,
        [Description("The requester of the requisition")] string requester,
        CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        _tools.Record(ToolNames.CreateRequisitionDraft, new Dictionary<string, object?>
        {
            ["supplierCode"] = supplierCode,
            ["item"] = item,
            ["description"] = description,
            ["quantity"] = quantity,
            ["date"] = date,
            ["requester"] = requester,
        });

        var draft = new RequisitionDraft(supplierCode, item, description, quantity, date, requester);

        // Reuse the persistence DTO's validation so staging and writing agree
        // on what "valid" means.
        var validationError = NewPurchaseRequisition.From(draft).Validate();
        if (validationError is not null)
        {
            _tools.Log(ToolNames.CreateRequisitionDraft, CreateRequisitionArguments, startedAt, 0);
            return Task.FromResult(ToolDraftStaged.Rejected(validationError));
        }

        RequisitionDraftSessionState.Stage(_turnContext.State!, draft);
        _turnContext.DraftStaged = true;
        _turnContext.DraftPresented = true;

        _tools.Log(ToolNames.CreateRequisitionDraft, CreateRequisitionArguments, startedAt, 1);
        return Task.FromResult(ToolDraftStaged.Ok(
            ToolDraftFields.From(draft),
            "Draft staged. Present these exact values back to the user as a summary and ask for explicit confirmation; " +
            "do not call create_requisition yet."));
    }

    [Description(
        "Records that the user explicitly confirmed the staged requisition draft. Call it ONLY after the user has " +
        "answered yes to the draft summary you presented — pass the user's answer as 'yes'. A decline leaves the " +
        "draft open for revision. Only a recorded confirmation unlocks create_requisition.")]
    private Task<ToolDraftConfirmation> ConfirmRequisitionDraftAsync(
        [Description("The user's answer to the draft confirmation, for example yes or no")] string answer,
        CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        _tools.Record(ToolNames.ConfirmRequisitionDraft, new Dictionary<string, object?>
        {
            ["answer"] = answer,
        });

        var state = _turnContext.State!;
        var snapshot = RequisitionDraftSessionState.Read(state);

        if (snapshot.Draft is null)
        {
            _tools.Log(ToolNames.ConfirmRequisitionDraft, ["answer"], startedAt, 0);
            return Task.FromResult(ToolDraftConfirmation.Declined(
                "There is no staged requisition draft in this session. Call create_requisition_draft with the six " +
                "fields first, then ask the user to confirm."));
        }

        if (!snapshot.Presented)
        {
            _tools.Log(ToolNames.ConfirmRequisitionDraft, ["answer"], startedAt, 0);
            return Task.FromResult(ToolDraftConfirmation.Declined(
                "The draft has not been presented to the user yet. Present the draft as a summary and ask for " +
                "confirmation before recording it."));
        }

        if (!IsAffirmative(answer))
        {
            _tools.Log(ToolNames.ConfirmRequisitionDraft, ["answer"], startedAt, 0);
            return Task.FromResult(ToolDraftConfirmation.Declined(
                $"'{answer}' is not an explicit confirmation, so the draft stays unconfirmed. Keep the draft open and " +
                "ask the user what to change, or whether they want to proceed."));
        }

        if (!RequisitionDraftSessionState.MarkConfirmed(state))
        {
            _tools.Log(ToolNames.ConfirmRequisitionDraft, ["answer"], startedAt, 0);
            return Task.FromResult(ToolDraftConfirmation.Declined(
                "The draft could not be confirmed. Present the draft again and ask the user to confirm."));
        }

        var confirmed = RequisitionDraftSessionState.Read(state).Draft!;
        _turnContext.DraftConfirmed = true;
        _tools.Log(ToolNames.ConfirmRequisitionDraft, ["answer"], startedAt, 1);
        return Task.FromResult(ToolDraftConfirmation.Recorded(
            ToolDraftFields.From(confirmed),
            "Confirmation recorded. Call create_requisition with exactly these values to persist the requisition."));
    }

    [Description(
        "Persists a new purchase requisition in the database. It REQUIRES a draft the user already confirmed: first " +
        "call create_requisition_draft, present the draft and ask the user to confirm, then call " +
        "confirm_requisition_draft with their yes, and only then call this tool with the same six values. " +
        "Calling it without a confirmed draft is refused and nothing is written. " +
        "Required parameters: supplierCode, item, description, quantity (a positive number), date (ISO format yyyy-MM-dd), requester. " +
        "Refuses to create a requisition when no existing requisition has the same item + supplier combination.")]
    private async Task<ToolRequisitionWrite> CreateRequisitionAsync(
        [Description("The code of the supplier")] string supplierCode,
        [Description("The code of the item")] string item,
        [Description("The description of the requisition")] string description,
        [Description("The quantity of the item")] decimal quantity,
        [Description("The date of the requisition")] string date,
        [Description("The requester of the requisition")] string requester,
        CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        _tools.Record(ToolNames.CreateRequisition, new Dictionary<string, object?>
        {
            ["supplierCode"] = supplierCode,
            ["item"] = item,
            ["description"] = description,
            ["quantity"] = quantity,
            ["date"] = date,
            ["requester"] = requester,
        });

        var state = _turnContext.State!;
        var snapshot = RequisitionDraftSessionState.Read(state);

        // Gate: no confirmed draft, no requisition. This is enforced here rather
        // than left to the model because the confirmation procedure is advisory
        // text and the model demonstrably skips it.
        if (!snapshot.HasConfirmedDraft)
        {
            var missingStep = snapshot.Draft is null
                ? ToolNames.CreateRequisitionDraft
                : ToolNames.ConfirmRequisitionDraft;
            var detail = snapshot.Draft is null
                ? "No requisition draft is staged in this session."
                : "A draft is staged but the user has not confirmed it.";

            _tools.Log(ToolNames.CreateRequisition, CreateRequisitionArguments, startedAt, 0);
            return ToolRequisitionWrite.RefusedForMissingStep(
                missingStep,
                $"Cannot create requisition: {detail} Call {ToolNames.CreateRequisitionDraft} to stage the draft, present it, " +
                $"then {ToolNames.ConfirmRequisitionDraft} to record the user's explicit yes, before calling " +
                $"{ToolNames.CreateRequisition}. Nothing was created.");
        }

        var draft = snapshot.Draft!;
        var conflict = FindConflict(draft, supplierCode, item, description, quantity, date, requester);
        if (conflict is not null)
        {
            _tools.Log(ToolNames.CreateRequisition, CreateRequisitionArguments, startedAt, 0);
            return ToolRequisitionWrite.RefusedForConflict(
                conflict.Value.Field,
                conflict.Value.Expected,
                $"Cannot create requisition: the argument '{conflict.Value.Field}' does not match the confirmed " +
                $"draft, whose value is '{conflict.Value.Expected}'. Call {ToolNames.CreateRequisitionDraft} with the " +
                $"corrected values, present it, and have the user confirm again. Nothing was created.");
        }

        var requisition = NewPurchaseRequisition.From(draft);

        var validationError = requisition.Validate();
        if (validationError is not null)
        {
            _tools.Log(ToolNames.CreateRequisition, CreateRequisitionArguments, startedAt, 0);
            return ToolRequisitionWrite.Rejected(validationError);
        }

        var combinationExists = await _repository.ExistsItemSupplierCombinationAsync(
            requisition.Item,
            requisition.SupplierCode,
            cancellationToken);

        if (!combinationExists)
        {
            _tools.Log(ToolNames.CreateRequisition, CreateRequisitionArguments, startedAt, 0);
            return ToolRequisitionWrite.Rejected(
                $"Cannot create requisition: supplier {requisition.SupplierCode} has no recorded requisition for item {requisition.Item}. " +
                "The supplier is not registered for that item, so no requisition was created. " +
                "Ask the user to confirm the item and supplier.");
        }

        var result = await _requisitionWriter.WriteAsync(
            requisition,
            _turnContext.SessionId,
            cancellationToken);

        _tools.Log(ToolNames.CreateRequisition, CreateRequisitionArguments, startedAt, result.Success ? 1 : 0);

        if (result.Success)
        {
            SkillSessionState.Clear(state);
            RequisitionDraftSessionState.Clear(state);
            _turnContext.RequisitionPersisted = true;
            return ToolRequisitionWrite.Created(result.RequisitionId!);
        }

        return ToolRequisitionWrite.Rejected(result.Error!);
    }

    private static bool IsAffirmative(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return false;
        }

        var normalized = answer.Trim().TrimEnd('!', '.', ' ').ToLowerInvariant();
        return normalized is "yes" or "y" or "yeah" or "yep" or "yes please" or "confirm" or "confirmed"
            or "approve" or "approved" or "go ahead" or "proceed";
    }

    /// <summary>
    /// Returns the first argument that disagrees with the confirmed draft, or
    /// null when the arguments match it exactly.
    /// </summary>
    private static (string Field, string Expected)? FindConflict(
        RequisitionDraft draft,
        string supplierCode,
        string item,
        string description,
        decimal quantity,
        string date,
        string requester)
    {
        if (!string.Equals(draft.SupplierCode, supplierCode, StringComparison.Ordinal))
        {
            return ("supplierCode", draft.SupplierCode);
        }

        if (!string.Equals(draft.Item, item, StringComparison.Ordinal))
        {
            return ("item", draft.Item);
        }

        if (!string.Equals(draft.Description, description, StringComparison.Ordinal))
        {
            return ("description", draft.Description);
        }

        if (draft.Quantity != quantity)
        {
            return ("quantity", draft.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (!string.Equals(draft.Date, date, StringComparison.Ordinal))
        {
            return ("date", draft.Date);
        }

        if (!string.Equals(draft.Requester, requester, StringComparison.Ordinal))
        {
            return ("requester", draft.Requester);
        }

        return null;
    }
}
