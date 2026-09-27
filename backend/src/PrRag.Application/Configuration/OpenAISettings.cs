namespace PrRag.Application.Configuration;

public sealed class OpenAISettings
{
    public const string SectionName = "OpenAI";

    public string ApiKey { get; set; } = string.Empty;
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
    public string ChatModel { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// Selects the provider serving both clients. See <see cref="LlmProviderNames"/>.
    /// The section stays named <c>OpenAI</c> because the keys are literally true:
    /// Azure OpenAI is reached through the same OpenAI SDK, and the models are
    /// still OpenAI models. Only the host and the deployment differ.
    /// </summary>
    public string Provider { get; set; } = LlmProviderNames.OpenAi;

    /// <summary>
    /// Required for <see cref="LlmProviderNames.Azure"/> and unused for
    /// <see cref="LlmProviderNames.OpenAi"/>, which has its own default host.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;
}
