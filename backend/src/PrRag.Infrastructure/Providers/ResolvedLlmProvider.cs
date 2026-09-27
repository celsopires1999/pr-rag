namespace PrRag.Infrastructure.Providers;

/// <summary>
/// The provider configuration after validation, and the single value the rest of
/// the Infrastructure layer reads. It carries no provider SDK type, so the
/// resolver stays a pure function of the settings and the endpoint it produces
/// can be asserted on directly. The chat and embedding values are model names on
/// OpenAI and deployment names on Azure; they are kept under their setting names
/// because rewriting them here would hide which of the two a caller supplied.
/// </summary>
public sealed record ResolvedLlmProvider(
    string Provider,
    Uri? Endpoint,
    string ApiKey,
    string ChatModel,
    string EmbeddingModel);
