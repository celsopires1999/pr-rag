using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using PrRag.Application.DTOs;

namespace PrRag.Application.Services.Agents.Specialists;

/// <summary>
/// The registration, bookkeeping, and logging mechanics every capability unit
/// shares. Composed rather than inherited, so each unit's annotated handler
/// methods sit directly beside the <c>Add</c> calls that register them and the
/// <c>[Description]</c> attributes that document them stay visible.
///
/// Every unit writes to the same scoped <see cref="AgentTurnContext"/>, so a
/// tool call is recorded identically no matter which capability owns the tool.
/// That is what keeps the observability report byte-identical across the split.
/// </summary>
public sealed class SpecialistToolSet
{
    private readonly AgentTurnContext _turnContext;
    private readonly ILogger<SpecialistToolSet> _logger;
    private readonly List<AITool> _tools = [];

    public SpecialistToolSet(AgentTurnContext turnContext, ILogger<SpecialistToolSet> logger)
    {
        _turnContext = turnContext;
        _logger = logger;
    }

    /// <summary>
    /// Every tool registered through this set, across all capability units. This
    /// is the whole registered set, not one unit's share — each unit keeps its own
    /// list of what it added, because a unit's definition must describe only its
    /// own tools.
    /// </summary>
    public IList<AITool> All => _tools;

    /// <summary>
    /// Registers a tool from its <c>[Description]</c>-annotated handler method
    /// group and returns it, so the caller can record it as its own.
    ///
    /// Pass the handler method itself, never a forwarding lambda:
    /// <c>AIFunctionFactory</c> derives the JSON schema from the registered
    /// delegate's <c>MethodInfo</c>, so a lambda silently drops every parameter
    /// <c>[Description]</c> and the model stops seeing them. No
    /// <c>Description</c> is passed here on purpose — the attribute on the
    /// method is the single source of the tool description.
    /// </summary>
    public AITool Add(string wireName, Delegate handler)
    {
        var tool = AIFunctionFactory.Create(handler, new AIFunctionFactoryOptions { Name = wireName });
        _tools.Add(tool);
        return tool;
    }

    /// <summary>
    /// Records a tool invocation for the observability report. This is report
    /// bookkeeping, not tracing — it is what makes the report's tool list
    /// independent of how the capabilities are grouped.
    /// </summary>
    public void Record(string name, IDictionary<string, object?> arguments) =>
        _turnContext.ToolCalls.Add(new RagToolCall
        {
            Name = name,
            Arguments = new Dictionary<string, object?>(arguments),
        });

    public void Log(string name, string[] argumentNames, long startedAt, int resultCount) =>
        _logger.LogInformation(
            "Tool {ToolName} invoked with {Arguments} returned {ResultCount} item(s) in {ElapsedMs}ms",
            name,
            argumentNames,
            resultCount,
            (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
}
