using PrRag.Application.Configuration;

namespace PrRag.Infrastructure.Providers;

/// <summary>
/// Turns the configured provider settings into a validated
/// <see cref="ResolvedLlmProvider"/>, or throws naming the configuration key at
/// fault.
/// </summary>
/// <remarks>
/// Every failure here is a configuration fault, and the reason it is raised at
/// startup rather than left to the first request is that a client built from bad
/// configuration does not fail loudly: it throws per-request, the workflow absorbs
/// that into an empty run, and the API answers <c>502</c> — the same signal a dead
/// provider or a rejected key produces. A fault that reads as an outage is a fault
/// nobody can diagnose, so it is refused before the host serves anything.
/// </remarks>
public static class LlmProviderResolver
{
    /// <summary>
    /// The path shape Azure OpenAI's OpenAI-compatible surface requires. The
    /// endpoint without it resolves to a path that does not exist and returns a
    /// <c>404</c>, which is again indistinguishable from a dead provider. Kept as
    /// one named predicate with the surface it encodes named beside it, so a
    /// future shape is a one-line change rather than a hunt.
    /// </summary>
    private const string AzureRequiredPathSuffix = "/openai/v1";

    public static ResolvedLlmProvider Resolve(OpenAISettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var provider = ResolveProvider(settings.Provider);
        var apiKey = RequireApiKey(settings.ApiKey);
        var endpoint = ResolveEndpoint(provider, settings.Endpoint);

        return new ResolvedLlmProvider(
            provider,
            endpoint,
            apiKey,
            settings.ChatModel,
            settings.EmbeddingModel);
    }

    private static string ResolveProvider(string? configured)
    {
        // A blank discriminator means "not configured", the same reading an absent
        // section property gets. Treating it as an unknown provider would fail
        // startup over a variable someone set to empty on purpose.
        var value = string.IsNullOrWhiteSpace(configured)
            ? LlmProviderNames.OpenAi
            : configured.Trim();

        var match = LlmProviderNames.All.FirstOrDefault(
            known => string.Equals(known, value, StringComparison.OrdinalIgnoreCase));

        return match ?? throw new InvalidOperationException(
            $"{ProviderKey} is set to '{value}', which is not a supported provider. " +
            $"Set it to one of: {string.Join(", ", LlmProviderNames.All)}.");
    }

    private static string RequireApiKey(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"{ApiKeyKey} is not set. The selected provider needs a credential, and it is " +
                "read from the environment so it never has to appear in the repository.");
        }

        return configured.Trim();
    }

    private static Uri? ResolveEndpoint(string provider, string? configured)
    {
        var value = string.IsNullOrWhiteSpace(configured) ? string.Empty : configured.Trim();

        if (provider == LlmProviderNames.OpenAi)
        {
            // Not ignored silently. An operator who set an endpoint expects the
            // requests to go there, and dropping it would send them to the default
            // host instead — a configuration fault that presents as an outage.
            if (value.Length > 0)
            {
                throw new InvalidOperationException(
                    $"{EndpointKey} is set but the '{LlmProviderNames.OpenAi}' provider does not use " +
                    "one; it has its own default host. Clear the setting, or set " +
                    $"{ProviderKey} to '{LlmProviderNames.Azure}'.");
            }

            return null;
        }

        if (value.Length == 0)
        {
            throw new InvalidOperationException(
                $"{EndpointKey} is required when {ProviderKey} is '{LlmProviderNames.Azure}'. Set it to " +
                "the Azure OpenAI endpoint, for example https://<resource>.openai.azure.com/openai/v1.");
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint)
            || !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !endpoint.AbsolutePath.TrimEnd('/').EndsWith(
                AzureRequiredPathSuffix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{EndpointKey} is set to '{value}', which is not a usable Azure OpenAI endpoint. " +
                "It must be an absolute https URL ending in " + AzureRequiredPathSuffix + ".");
        }

        return endpoint;
    }

    private const string ProviderKey = "OpenAI__Provider";
    private const string EndpointKey = "OpenAI__Endpoint";
    private const string ApiKeyKey = "OpenAI__ApiKey";
}
