using System.Text;
using Microsoft.Extensions.AI;
using PrRag.Application.Abstractions;

namespace PrRag.Application.Services;

public sealed class SemanticQueryRewriter : IQueryRewriter
{
    private const string SystemPrompt =
        """
        You are a query optimizer for semantic search.

        The search index stores purchase requisitions with these fields:
        Supplier Code, Supplier Name, Item, Item Name, Description.

        The user may ask in any language (Portuguese, English, etc.). The data
        is in English.

        Your task: rewrite the current user question into a short, keyword-rich
        query optimized for cosine similarity search against the fields above.

        Use the full conversation history to disambiguate references such as
        "that one", "the other", "as we saw earlier", etc. Resolve those
        references against the earlier turns and incorporate the resolved
        entities into the query.

        Rules:
        - Remove slang, pleasantries, and filler words
        - Translate to English if the question is in another language
        - Focus on entity names, concepts, and field values
        - Do NOT include field labels like "supplier_code:" - just the values
        - Return ONLY the optimized query, nothing else

        Examples:
        Input:
        Conversation:
        [User] opa me diz quais PRs tem pra aquela bomba hidraulica da Acme
        Query: bomba hidraulica
        Output: hydraulic pump Acme

        Input:
        Conversation:
        [User] me mostra as PRs do fornecedor Acme
        [User] e daquele outro que a gente viu antes
        Query: aquele outro que vimos antes
        Output: Acme supplier purchase requisition

        Input: "ITM-00000000000000000001"
        Output: "ITM-00000000000000000001"
        """;

    private readonly IChatClient _chatClient;

    public SemanticQueryRewriter(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public async Task<string> RewriteAsync(
        string question,
        IReadOnlyList<ChatMessage> conversation,
        CancellationToken cancellationToken = default)
    {
        var prompt = new StringBuilder()
            .AppendLine(SystemPrompt)
            .AppendLine()
            .AppendLine("Input:")
            .AppendLine("Conversation:");

        foreach (var msg in conversation)
        {
            if (msg.Role == ChatRole.System || string.IsNullOrWhiteSpace(msg.Text))
            {
                continue;
            }

            var role = msg.Role == ChatRole.Assistant ? "Assistant" : "User";
            prompt.AppendLine($"[{role}] {msg.Text}");
        }

        prompt
            .AppendLine($"Query: {question}")
            .Append("Output: ");

        var response = await _chatClient.GetResponseAsync(prompt.ToString(), cancellationToken: cancellationToken);

        return response.Text.Trim();
    }
}
