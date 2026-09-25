using Microsoft.Extensions.AI;

namespace PrRag.Tests;

/// <summary>
/// Scripted tool calls for the requisition creation flow. Requisition
/// persistence is gated on a staged, user-confirmed draft, so a test that
/// expects a row to be written has to script
/// <see cref="Draft"/> → <see cref="Confirm"/> → <see cref="Create"/>.
/// A test that deliberately omits one of those steps is testing the gate.
/// </summary>
internal static class RequisitionFlow
{
    public const string SupplierCode = "SUP000001";

    public const string Item = "ITM0001";

    public const string Description = "Hydraulic pump for maintenance.";

    public const decimal Quantity = 3m;

    public const string Date = "2026-10-01";

    public const string Requester = "Ana Souza";

    public static FunctionCallContent Draft(
        string supplierCode = SupplierCode,
        string item = Item,
        string description = Description,
        decimal quantity = Quantity,
        string date = Date,
        string requester = Requester,
        string callId = "call_draft") =>
        new(callId, "create_requisition_draft", new Dictionary<string, object?>
        {
            ["supplierCode"] = supplierCode,
            ["item"] = item,
            ["description"] = description,
            ["quantity"] = quantity,
            ["date"] = date,
            ["requester"] = requester,
        });

    public static FunctionCallContent Confirm(string answer = "yes", string callId = "call_confirm") =>
        new(callId, "confirm_requisition_draft", new Dictionary<string, object?>
        {
            ["answer"] = answer,
        });

    public static FunctionCallContent Create(
        string supplierCode = SupplierCode,
        string item = Item,
        string description = Description,
        decimal quantity = Quantity,
        string date = Date,
        string requester = Requester,
        string callId = "call_create") =>
        new(callId, "create_requisition", new Dictionary<string, object?>
        {
            ["supplierCode"] = supplierCode,
            ["item"] = item,
            ["description"] = description,
            ["quantity"] = quantity,
            ["date"] = date,
            ["requester"] = requester,
        });

    /// <summary>Stages and confirms a draft with the default values.</summary>
    public static void ScriptConfirmedDraft(
        List<FunctionCallContent> script,
        string supplierCode = SupplierCode,
        string item = Item,
        string description = Description,
        decimal quantity = Quantity,
        string date = Date,
        string requester = Requester,
        string callIdSuffix = "")
    {
        script.Add(Draft(supplierCode, item, description, quantity, date, requester, $"call_draft{callIdSuffix}"));
        script.Add(Confirm(callId: $"call_confirm{callIdSuffix}"));
    }

    public static void ScriptConfirmedCreation(
        List<FunctionCallContent> script,
        string supplierCode = SupplierCode,
        string item = Item,
        string description = Description,
        decimal quantity = Quantity,
        string date = Date,
        string requester = Requester,
        string callIdSuffix = "")
    {
        ScriptConfirmedDraft(script, supplierCode, item, description, quantity, date, requester, callIdSuffix);
        script.Add(Create(
            supplierCode,
            item,
            description,
            quantity,
            date,
            requester,
            $"call_create{callIdSuffix}"));
    }
}
