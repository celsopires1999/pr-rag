using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;

namespace PrRag.Infrastructure.Providers;

/// <summary>
/// Builds the chat and embedding clients from a resolved provider. This is the
/// only place in the solution that names a provider SDK: the application layer,
/// the agent graph, and the report all speak only the abstractions this returns.
/// </summary>
/// <remarks>
/// Both providers are reached through the same SDK types, because the OpenAI SDK
/// already carries Azure OpenAI support over its <c>/openai/v1</c> surface — the
/// endpoint on the options is the whole difference, and the model argument is the
/// deployment name on Azure. That is why there is no second client type here and
/// no second package on the project.
/// </remarks>
public static class LlmClientFactory
{
    public static (IChatClient Chat, IEmbeddingGenerator<string, Embedding<float>> Embeddings) Create(
        ResolvedLlmProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var credential = new ApiKeyCredential(provider.ApiKey);
        var chat = new ChatClient(provider.ChatModel, credential, OptionsFor(provider));
        var embeddings = new EmbeddingClient(provider.EmbeddingModel, credential, OptionsFor(provider));

        return (chat.AsIChatClient(), embeddings.AsIEmbeddingGenerator());
    }

    private static OpenAIClientOptions OptionsFor(ResolvedLlmProvider provider) =>
        provider.Endpoint is null
            ? new OpenAIClientOptions()
            : new OpenAIClientOptions { Endpoint = provider.Endpoint };
}
