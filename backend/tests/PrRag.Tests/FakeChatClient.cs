using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using PrRag.Application.Services.Agents;

namespace PrRag.Tests;

public sealed class FakeChatClient : IChatClient
{
    private readonly object _lock = new();
    private readonly List<ChatMessage> _messages = new();
    private string _lastPrompt = string.Empty;
    private int _calls;

    public string Answer { get; set; } = "fake answer";

    /// <summary>
    /// When set, the chat model is scripted to issue this tool call on its first
    /// turn (exactly once), then answer with <see cref="Answer"/> on a subsequent turn.
    /// </summary>
    public FunctionCallContent? ToolCall { get; set; }

    /// <summary>
    /// Scripted tool calls issued one per turn, in order, across calls to
    /// <see cref="GetResponseAsync"/>. When exhausted, the model answers with
    /// <see cref="Answer"/>. Takes precedence over <see cref="ToolCall"/>.
    /// </summary>
    public List<FunctionCallContent> ScriptedToolCalls { get; } = new();

    /// <summary>
    /// Per-agent tool-call scripts, keyed by a tool name that only the agent in
    /// question is offered.
    ///
    /// <para>
    /// The single <see cref="ScriptedToolCalls"/> queue cannot express a handoff
    /// graph: with capabilities extracted, the orchestrator and a specialist are
    /// separate agents with disjoint tool sets, and a retrieval turn is three
    /// calls across two agents — the orchestrator handing off, the specialist
    /// running the search, the orchestrator answering. Keying on an offered tool
    /// name routes each reply to the agent that owns it, because the two tool
    /// sets do not overlap.
    /// </para>
    /// </summary>
    public Dictionary<string, List<FunctionCallContent>> ScriptedToolCallsByAgent { get; } =
        new(StringComparer.Ordinal);

    private int _toolCallConsumed;
    private int _scriptedIndex;
    private readonly Dictionary<string, int> _agentScriptIndex = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seenResultCallIds = new(StringComparer.Ordinal);

    /// <summary>
    /// MAF names the generated handoff tool positionally (<c>handoff_to_1</c>) and
    /// offers no overload to name it, so a test cannot script a handoff by writing
    /// that literal — adding a participant would renumber it. When this is set and
    /// the calling agent is offered such a tool with its own script exhausted, the
    /// fake hands off instead of answering, which is the default a real model
    /// reaches for.
    /// </summary>
    public bool AutoHandoff { get; set; }

    /// <summary>
    /// Which participant <see cref="AutoHandoff"/> transfers to, as a 1-based
    /// position among the participants the composer adds. Null takes the first
    /// handoff tool offered, which is only right while a single specialist can
    /// serve every handoff.
    ///
    /// <para>
    /// The number is positional because the tool name MAF generates is. Tests
    /// never compute it themselves — <c>HandoffScripting.HandOffTo</c> derives it
    /// from the catalog, so this type of change is a one-line edit in one place
    /// rather than a silent breakage of every handoff test.
    /// </para>
    /// </summary>
    public int? AutoHandoffParticipant { get; set; }

    // The positional convention is the application's (HandoffToolName), because the
    // report resolves real handoffs through it. A second copy here could disagree
    // with the production mapping and a test would then pass against a wire name no
    // handoff ever uses.
    private const string HandoffToolPrefix = "handoff_to_";

    /// <summary>The handoff tool the last call was offered, if any.</summary>
    public string? LastHandoffToolName { get; private set; }

    /// <summary>
    /// Rewinds the script so a later turn of the same session can be scripted
    /// from the start. The client is a singleton shared across scopes, so
    /// <see cref="ScriptedToolCalls"/> alone cannot express "turn 2 does this".
    /// </summary>
    public void ResetScript()
    {
        lock (_lock)
        {
            ScriptedToolCalls.Clear();
            ScriptedToolCallsByAgent.Clear();
            _scriptedIndex = 0;
            _toolCallConsumed = 0;
            _agentScriptIndex.Clear();
        }
    }

    /// <summary>
    /// Queues a tool call for whichever agent is offered <paramref name="agentKeyTool"/>.
    /// Must be a tool name unique to that agent — the orchestrator is offered the
    /// handoff tool plus the capabilities it still holds, and each specialist is
    /// offered only its own.
    /// </summary>
    public FakeChatClient ScriptFor(string agentKeyTool, params FunctionCallContent[] calls)
    {
        if (!ScriptedToolCallsByAgent.TryGetValue(agentKeyTool, out var queue))
        {
            queue = [];
            ScriptedToolCallsByAgent[agentKeyTool] = queue;
        }

        queue.AddRange(calls);
        return this;
    }

    public string LastPrompt
    {
        get { lock (_lock) { return _lastPrompt; } }
    }

    public int CallCount
    {
        get { lock (_lock) { return _calls; } }
    }

    public IReadOnlyList<ChatMessage> LastMessages
    {
        get { lock (_lock) { return _messages.ToList(); } }
    }

    /// <summary>
    /// Tool names the model was offered on the most recent call. Diagnostic:
    /// with a handoff graph the offered set differs per agent, so this is how a
    /// test can tell which agent the graph actually ran.
    /// </summary>
    public List<string> LastOfferedToolNames { get; } = new();

    /// <summary>
    /// Whether the response repeats the request, as MAF's does. Off by default:
    /// most tests only care about the reply, and a few assert on the exact
    /// contents of the last call's messages.
    /// </summary>
    public bool EchoRequest { get; set; }

    /// <summary>The offered tool set of every call, in order, one entry per agent turn.</summary>
    public List<List<string>> AllOfferedToolNames { get; } = new();

    /// <summary>The instructions the model was given on every call, in order.</summary>
    public List<string> AllPrompts { get; } = new();

    /// <summary>
    /// The instructions given to the agent that holds <paramref name="discriminatorTool"/>,
    /// on its most recent call.
    ///
    /// <para>
    /// <see cref="LastPrompt"/> is only "the prompt" while there is one agent. Once a
    /// capability moves to a specialist, the last caller is whichever agent finished
    /// the turn, so a test asserting on the orchestrator's instructions has to ask for
    /// the orchestrator by something it holds rather than by position.
    /// </para>
    /// </summary>
    public string PromptForAgent(string discriminatorTool)
    {
        for (var i = AllPrompts.Count - 1; i >= 0; i--)
        {
            if (i < AllOfferedToolNames.Count && AllOfferedToolNames[i].Contains(discriminatorTool))
            {
                return AllPrompts[i];
            }
        }

        throw new InvalidOperationException(
            $"No call was offered '{discriminatorTool}', so that agent never ran. " +
            $"Offered per call: {string.Join(" / ", AllOfferedToolNames.Select(t => string.Join(",", t)))}");
    }

    /// <summary>
    /// Every tool-role message seen across every call, oldest first.
    ///
    /// <para>
    /// A handoff graph makes <see cref="LastMessages"/> the wrong place to look for
    /// a tool result: the final call belongs to the orchestrator resuming after the
    /// handoff, so the specialist's tool result is not in its message list. Tests
    /// assert against this instead, which holds the results of all agents.
    /// </para>
    /// </summary>
    public List<ChatMessage> AllToolMessages { get; } = new();

    /// <summary>
    /// The most recent tool result the agent loop produced, normalized to JSON
    /// text so assertions do not depend on which concrete type the AI function
    /// layer's result marshalling happened to produce.
    /// </summary>
    public string LastToolResultJson()
    {
        return ToolResultJson(AllToolMessages.Count - 1);
    }

    /// <summary>
    /// Records the tool results the agent actually produced, keyed by call id.
    ///
    /// <para>
    /// The application now owns the conversation, so a turn's request messages
    /// contain every earlier turn — including that turn's tool results. Recording
    /// whatever appears in a tool-role message would therefore report old calls
    /// again, and "the last tool result" would stop meaning the last call. A call
    /// id is recorded once, on the turn that made it.
    /// </para>
    /// </summary>
    private void RecordFreshToolResults(IList<ChatMessage> messages)
    {
        foreach (var message in messages)
        {
            if (message.Role != ChatRole.Tool)
            {
                continue;
            }

            foreach (var result in message.Contents.OfType<FunctionResultContent>())
            {
                if (_seenResultCallIds.Add(result.CallId ?? string.Empty))
                {
                    AllToolMessages.Add(message);
                }
            }
        }
    }

    /// <summary>
    /// The JSON result of the <paramref name="index"/>-th tool invocation across
    /// all agents on this turn, so a specialist's result is reachable after the
    /// orchestrator has resumed.
    /// </summary>
    public string ToolResultJson(int index)
    {
        var results = AllToolMessages
            .SelectMany(m => m.Contents.OfType<FunctionResultContent>())
            .ToList();
        return Normalize(results[index].Result);
    }

    private static string Normalize(object? result)
    {
        return result switch
        {
            string text => text,
            JsonElement element => element.GetRawText(),
            JsonNode node => node.ToJsonString(),
            _ => JsonSerializer.Serialize(result),
        };
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = messages.ToList();
        var prompt = BuildPromptText(snapshot, options);
        lock (_lock)
        {
            _calls++;
            _lastPrompt = prompt;
            _messages.Clear();
            _messages.AddRange(snapshot.Select(m => m));
            LastOfferedToolNames.Clear();
            LastOfferedToolNames.AddRange(options?.Tools?.Select(t => t.Name) ?? []);
            AllOfferedToolNames.Add(LastOfferedToolNames.ToList());
            AllPrompts.Add(LastPrompt);
            RecordFreshToolResults(snapshot);
        }

        ChatMessage reply;
        var offered = options?.Tools?.Select(t => t.Name).ToList() ?? [];

        if (TryTakeAgentScript(offered, out var agentCall))
        {
            reply = new ChatMessage(ChatRole.Assistant, new List<AIContent> { agentCall });
        }
        else if (_scriptedIndex < ScriptedToolCalls.Count)
        {
            reply = new ChatMessage(ChatRole.Assistant, new List<AIContent> { ScriptedToolCalls[_scriptedIndex++] });
        }
        else if (TryTakeAutoHandoff(offered, out var handoff))
        {
            reply = new ChatMessage(ChatRole.Assistant, new List<AIContent> { handoff });
        }
        else if (ToolCall is { } call && Interlocked.CompareExchange(ref _toolCallConsumed, 1, 0) == 0)
        {
            reply = new ChatMessage(ChatRole.Assistant, new List<AIContent> { call });
        }
        else
        {
            reply = new ChatMessage(ChatRole.Assistant, Answer);
        }

        return Task.FromResult(new ChatResponse
        {
            // MAF's agent response carries the whole turn, not just the new
            // message: the request it was given plus the reply. <see
            // cref="EchoRequest"/> reproduces that, because a fake returning only
            // the reply would hide any code that records the response back into
            // the conversation — which is how the history once doubled.
            Messages = EchoRequest ? [.. snapshot, reply] : [reply],
        });
    }

    /// <summary>
    /// Returns the next scripted call for the agent that was called, identified by
    /// which discriminator tool it was offered. An agent whose queue is exhausted
    /// falls through to the default answer, which is how the entry agent ends a
    /// handoff turn with the specialist's results already in hand.
    /// </summary>
    private bool TryTakeAgentScript(List<string> offeredTools, out FunctionCallContent call)
    {
        call = null!;
        var offered = offeredTools.ToHashSet(StringComparer.Ordinal);

        foreach (var (key, queue) in ScriptedToolCallsByAgent)
        {
            if (!offered.Contains(key))
            {
                continue;
            }

            var index = _agentScriptIndex.GetValueOrDefault(key);
            if (index >= queue.Count)
            {
                continue;
            }

            _agentScriptIndex[key] = index + 1;
            call = queue[index];
            return true;
        }

        return false;
    }

    /// <summary>
    /// The calling agent has nothing left scripted, so if it is offered a handoff
    /// tool and auto-handoff is on, transfer rather than answer.
    ///
    /// <para>
    /// Checked after the flat <see cref="ScriptedToolCalls"/> queue, not before it.
    /// That queue is the entry agent's script — it names tools the entry agent
    /// holds, such as <c>activate_skill</c> — so an entry-agent call must not be
    /// skipped over by a handoff the test also asked for. Per-agent scripts are
    /// still consulted first, since a specialist's tools are only ever on offer to
    /// the specialist.
    /// </para>
    /// </summary>
    private bool TryTakeAutoHandoff(List<string> offeredTools, out FunctionCallContent call)
    {
        call = null!;
        if (!AutoHandoff)
        {
            return false;
        }

        var handoff = SelectHandoffTool(offeredTools);
        if (handoff is null)
        {
            return false;
        }

        LastHandoffToolName = handoff;
        call = new FunctionCallContent($"handoff_{Guid.NewGuid():N}", handoff, new Dictionary<string, object?>());
        return true;
    }

    private string? SelectHandoffTool(List<string> offeredTools)
    {
        if (AutoHandoffParticipant is null)
        {
            return offeredTools.FirstOrDefault(n => n.StartsWith(HandoffToolPrefix, StringComparison.Ordinal));
        }

        var wanted = HandoffToolName.ForParticipant(AutoHandoffParticipant.Value);
        return offeredTools.Contains(wanted, StringComparer.Ordinal)
            ? wanted
            : null;
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var message in response.Messages)
        {
            foreach (var content in message.Contents)
            {
                switch (content)
                {
                    case FunctionCallContent call:
                        yield return new ChatResponseUpdate
                        {
                            Role = ChatRole.Assistant,
                            Contents = new[] { call },
                        };
                        break;
                    case TextContent text:
                        yield return new ChatResponseUpdate
                        {
                            Role = ChatRole.Assistant,
                            Contents = new[] { text },
                        };
                        break;
                }
            }
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    /// <summary>
    /// Everything the model is actually given: the messages plus
    /// <see cref="ChatOptions.Instructions"/>.
    ///
    /// The instructions channel matters because MAF's <c>ChatClientAgent</c> puts
    /// an agent's <c>Instructions</c> there rather than into a system message. A
    /// fake that only joined the message texts was therefore blind to the entire
    /// system prompt, and would have reported a prompt-driven failure as "no
    /// prompt was sent". Instructions are prepended so the ordering matches what
    /// a real client sends.
    /// </summary>
    private static string BuildPromptText(IReadOnlyList<ChatMessage> messages, ChatOptions? options)
    {
        var body = string.Join("\n", messages.Select(m => m.Text));
        return string.IsNullOrEmpty(options?.Instructions)
            ? body
            : options!.Instructions + "\n" + body;
    }
}
