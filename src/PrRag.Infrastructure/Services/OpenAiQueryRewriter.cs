using System.Text;
using Microsoft.Extensions.AI;
using PrRag.Application.Abstractions;

namespace PrRag.Infrastructure.Services;

public sealed class OpenAiQueryRewriter : IQueryRewriter
{
    private const string SystemPrompt =
        """
        You are a query optimizer for semantic search.

        The search index stores purchase requisitions with these fields:
        Supplier Code, Supplier Name, Item, Item Name, Description.

        The user may ask in any language (Portuguese, English, etc.). The data
        is in English.

        Your task: rewrite the user's question into a short, keyword-rich query
        optimized for cosine similarity search against the fields above.

        Rules:
        - Remove slang, pleasantries, and filler words
        - Translate to English if the question is in another language
        - Focus on entity names, concepts, and field values
        - Do NOT include field labels like "supplier_code:" - just the values
        - Return ONLY the optimized query, nothing else

        Examples:
        Input: "opa me diz quais PRs tem pra aquela bomba hidraulica da Acme"
        Output: "hydraulic pump Acme"

        Input: "tem alguma requisicao do fornecedor Acme?"
        Output: "purchase requisition Acme supplier"

        Input: "ITM-00000000000000000001"
        Output: "ITM-00000000000000000001"
        """;

    private readonly IChatClient _chatClient;

    public OpenAiQueryRewriter(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public async Task<string> RewriteAsync(
        string question,
        CancellationToken cancellationToken = default)
    {
        var prompt = new StringBuilder()
            .AppendLine(SystemPrompt)
            .AppendLine()
            .AppendLine($"Input: {question}")
            .Append("Output: ")
            .ToString();

        var response = await _chatClient.GetResponseAsync(prompt, cancellationToken: cancellationToken);

        return response.Text.Trim();
    }
}
