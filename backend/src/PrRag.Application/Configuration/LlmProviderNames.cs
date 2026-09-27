namespace PrRag.Application.Configuration;

/// <summary>
/// The provider values <see cref="OpenAISettings.Provider"/> accepts. Declared once,
/// here, because both the settings default and the Infrastructure resolver that
/// validates the value have to name the same vocabulary — a second list would
/// drift, and a drift here fails at startup rather than at the first request.
/// </summary>
public static class LlmProviderNames
{
    public const string OpenAi = "openai";
    public const string Azure = "azure";

    public static readonly IReadOnlyList<string> All = [OpenAi, Azure];
}
