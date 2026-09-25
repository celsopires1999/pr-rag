using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

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

    private int _toolCallConsumed;
    private int _scriptedIndex;

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
            _scriptedIndex = 0;
            _toolCallConsumed = 0;
        }
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
    /// The most recent tool result the agent loop produced, normalized to JSON
    /// text so assertions do not depend on which concrete type the AI function
    /// layer's result marshalling happened to produce.
    /// </summary>
    public string LastToolResultJson()
    {
        var toolMessage = LastMessages.Last(m => m.Role == ChatRole.Tool);
        var result = toolMessage.Contents.OfType<FunctionResultContent>().Last().Result;

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
        var prompt = string.Join("\n", snapshot.Select(m => m.Text));
        lock (_lock)
        {
            _calls++;
            _lastPrompt = prompt;
            _messages.Clear();
            _messages.AddRange(snapshot.Select(m => m));
        }

        ChatMessage reply;
        if (_scriptedIndex < ScriptedToolCalls.Count)
        {
            reply = new ChatMessage(ChatRole.Assistant, new List<AIContent> { ScriptedToolCalls[_scriptedIndex++] });
        }
        else if (ToolCall is { } call && Interlocked.CompareExchange(ref _toolCallConsumed, 1, 0) == 0)
        {
            reply = new ChatMessage(ChatRole.Assistant, new List<AIContent> { call });
        }
        else
        {
            reply = new ChatMessage(ChatRole.Assistant, Answer);
        }

        return Task.FromResult(new ChatResponse { Messages = new List<ChatMessage> { reply } });
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
}
