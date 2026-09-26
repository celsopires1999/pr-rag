namespace PrRag.Application.Services.Agents;

/// <summary>
/// Every tool wire name the chat model can see, declared exactly once.
///
/// The names are referenced by four different consumers that must never drift:
/// the tool's own registration, the per-turn <c>RecordToolCall</c> bookkeeping that
/// feeds the observability report, the <c>&lt;ALLOWED_ACTIONS&gt;</c> text in the
/// owning specialist's action block, and the shipped skill markdown. Keeping them
/// here rather than on an individual specialist is what makes a rename a single
/// edit and makes a duplicate claim impossible to introduce silently.
///
/// These values are load-bearing outside this codebase: the skill markdown and the
/// prompt name them literally, so a rename is a behavior change, not a refactor.
/// </summary>
public static class ToolNames
{
    public const string SearchByCodes = "search_by_codes";

    public const string SearchSemantic = "search_semantic";

    public const string ActivateSkill = "activate_skill";

    public const string GetSuppliersByItem = "get_suppliers_by_item";

    public const string CreateRequisitionDraft = "create_requisition_draft";

    public const string ConfirmRequisitionDraft = "confirm_requisition_draft";

    public const string CreateRequisition = "create_requisition";
}
