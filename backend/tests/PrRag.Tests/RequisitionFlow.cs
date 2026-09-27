using Microsoft.Extensions.AI;
using PrRag.Application.Services.Agents;

namespace PrRag.Tests;

/// <summary>
/// Scripted tool calls for the requisition creation flow. Requisition
/// persistence is gated on a staged, user-confirmed draft, so a test that
/// expects a row to be written has to script
/// <see cref="Draft"/> → <see cref="Confirm"/> → <see cref="Create"/>.
/// A test that deliberately omits one of those steps is testing the gate.
///
/// <para>
/// The three factories are routed onto the agent that owns the write tools and
/// the turn is handed off to it, because that is the shape the graph now has: a
/// creation turn enters at the orchestrator, which holds no write tool, and
/// reaches them through the creation specialist. The flat
/// <see cref="FakeChatClient.ScriptedToolCalls"/> queue cannot express that —
/// it would fire the first call at an agent the tool was never offered.
/// </para>
/// </summary>
internal static class RequisitionFlow
{
    public const string SupplierCode = "SUP000001";

    public const string Item = "ITM0001";

    public const string Description = "Hydraulic pump for maintenance.";

    public const decimal Quantity = 3m;

    public const string Date = "2026-10-01";

    public const string Requester = "Ana Souza";

    /// <summary>
    /// The tool used to identify the creation agent in a per-agent script. It is
    /// the only discriminator available: all three write tools are on that one
    /// agent, and no other agent is offered any of them.
    /// </summary>
    public const string CreationAgentKey = ToolNames.CreateRequisitionDraft;

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

    /// <summary>
    /// Hands the turn to the creation agent and scripts <paramref name="calls"/> on
    /// it. Call again after <see cref="FakeChatClient.ResetScript"/>; both halves
    /// are idempotent, so calling it once per scripted turn is enough.
    /// </summary>
    public static void OnCreationAgent(FakeChatClient fake, IServiceProvider provider, params FunctionCallContent[] calls)
    {
        fake.HandOffToCreation(provider);
        fake.ScriptFor(CreationAgentKey, calls);
    }

    /// <summary>Stages and confirms a draft with the default values.</summary>
    public static void ScriptConfirmedDraft(
        FakeChatClient fake,
        IServiceProvider provider,
        string supplierCode = SupplierCode,
        string item = Item,
        string description = Description,
        decimal quantity = Quantity,
        string date = Date,
        string requester = Requester,
        string callIdSuffix = "")
        => OnCreationAgent(
            fake,
            provider,
            Draft(supplierCode, item, description, quantity, date, requester, $"call_draft{callIdSuffix}"),
            Confirm(callId: $"call_confirm{callIdSuffix}"));

    public static void ScriptConfirmedCreation(
        FakeChatClient fake,
        IServiceProvider provider,
        string supplierCode = SupplierCode,
        string item = Item,
        string description = Description,
        decimal quantity = Quantity,
        string date = Date,
        string requester = Requester,
        string callIdSuffix = "")
    {
        ScriptConfirmedDraft(
            fake,
            provider,
            supplierCode,
            item,
            description,
            quantity,
            date,
            requester,
            callIdSuffix);

        fake.ScriptFor(
            CreationAgentKey,
            Create(supplierCode, item, description, quantity, date, requester, $"call_create{callIdSuffix}"));
    }
}
