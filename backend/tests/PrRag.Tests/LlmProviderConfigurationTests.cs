using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI.Chat;
using OpenAI.Embeddings;
using PrRag.Application.Configuration;
using PrRag.Infrastructure;
using PrRag.Infrastructure.Providers;
using Xunit;

namespace PrRag.Tests;

public class LlmProviderConfigurationTests
{
    private const string AzureEndpoint = "https://prrag-test.openai.azure.com/openai/v1";

    // -- Resolution ---------------------------------------------------------

    [Fact]
    public void An_unconfigured_provider_resolves_to_openai_with_no_endpoint()
    {
        var resolved = Resolve(new OpenAISettings { ApiKey = "key" });

        Assert.Equal(LlmProviderNames.OpenAi, resolved.Provider);
        Assert.Null(resolved.Endpoint);
    }

    [Fact]
    public void A_blank_provider_resolves_to_openai()
    {
        var resolved = Resolve(new OpenAISettings { ApiKey = "key", Provider = "  " });

        Assert.Equal(LlmProviderNames.OpenAi, resolved.Provider);
    }

    [Fact]
    public void The_provider_name_is_matched_regardless_of_case_or_padding()
    {
        var resolved = Resolve(new OpenAISettings
        {
            ApiKey = "key",
            Provider = "  Azure  ",
            Endpoint = AzureEndpoint,
        });

        Assert.Equal(LlmProviderNames.Azure, resolved.Provider);
    }

    [Fact]
    public void The_azure_endpoint_is_the_configured_uri()
    {
        var resolved = Resolve(new OpenAISettings
        {
            ApiKey = "key",
            Provider = LlmProviderNames.Azure,
            Endpoint = AzureEndpoint,
        });

        Assert.Equal(new Uri(AzureEndpoint), resolved.Endpoint);
    }

    [Fact]
    public void The_chat_and_embedding_settings_are_carried_through_verbatim()
    {
        var resolved = Resolve(new OpenAISettings
        {
            ApiKey = "key",
            Provider = LlmProviderNames.Azure,
            Endpoint = AzureEndpoint,
            ChatModel = "gpt-4o-mini-prod",
            EmbeddingModel = "embeddings-prod",
        });

        Assert.Equal("gpt-4o-mini-prod", resolved.ChatModel);
        Assert.Equal("embeddings-prod", resolved.EmbeddingModel);
    }

    // -- Validation ---------------------------------------------------------

    [Fact]
    public void An_unknown_provider_names_the_setting_and_the_recognised_values()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Resolve(
            new OpenAISettings { ApiKey = "key", Provider = "bedrock" }));

        Assert.Contains("OpenAI__Provider", error.Message);
        Assert.Contains("bedrock", error.Message);
        Assert.Contains(LlmProviderNames.OpenAi, error.Message);
        Assert.Contains(LlmProviderNames.Azure, error.Message);
    }

    [Fact]
    public void Azure_without_an_endpoint_names_the_endpoint_setting()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Resolve(
            new OpenAISettings { ApiKey = "key", Provider = LlmProviderNames.Azure }));

        Assert.Contains("OpenAI__Endpoint", error.Message);
    }

    [Theory]
    [InlineData("https://prrag-test.openai.azure.com")]
    [InlineData("https://prrag-test.openai.azure.com/openai")]
    [InlineData("https://prrag-test.openai.azure.com/openai/v2")]
    [InlineData("http://prrag-test.openai.azure.com/openai/v1")]
    [InlineData("prrag-test.openai.azure.com/openai/v1")]
    [InlineData("not a url")]
    public void An_azure_endpoint_of_the_wrong_shape_names_the_endpoint_setting(string endpoint)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Resolve(new OpenAISettings
        {
            ApiKey = "key",
            Provider = LlmProviderNames.Azure,
            Endpoint = endpoint,
        }));

        Assert.Contains("OpenAI__Endpoint", error.Message);
        Assert.Contains("/openai/v1", error.Message);
    }

    [Fact]
    public void A_blank_credential_names_the_key_setting()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Resolve(
            new OpenAISettings { ApiKey = "   " }));

        Assert.Contains("OpenAI__ApiKey", error.Message);
    }

    [Fact]
    public void A_blank_credential_is_rejected_on_the_azure_path_too()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Resolve(new OpenAISettings
        {
            Provider = LlmProviderNames.Azure,
            Endpoint = AzureEndpoint,
        }));

        Assert.Contains("OpenAI__ApiKey", error.Message);
    }

    [Fact]
    public void An_endpoint_set_for_a_provider_that_does_not_use_one_is_refused_rather_than_ignored()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Resolve(new OpenAISettings
        {
            ApiKey = "key",
            Provider = LlmProviderNames.OpenAi,
            Endpoint = AzureEndpoint,
        }));

        Assert.Contains("OpenAI__Endpoint", error.Message);
    }

    // -- Clients ------------------------------------------------------------

    [Theory]
    [InlineData(LlmProviderNames.OpenAi, null)]
    [InlineData(LlmProviderNames.Azure, AzureEndpoint)]
    public void Both_clients_are_built_for_either_provider(string provider, string? endpoint)
    {
        var (chat, embeddings) = LlmClientFactory.Create(Resolve(new OpenAISettings
        {
            ApiKey = "key",
            Provider = provider,
            Endpoint = endpoint ?? string.Empty,
            ChatModel = "chat-deployment",
            EmbeddingModel = "embeddings-deployment",
        }));

        Assert.NotNull(chat);
        Assert.NotNull(embeddings);
        Assert.IsType<ChatClient>(chat.GetService(typeof(ChatClient)));
        Assert.IsType<EmbeddingClient>(embeddings.GetService(typeof(EmbeddingClient)));
    }

    [Fact]
    public void A_consumer_resolves_the_chat_and_embedding_clients_unkeyed()
    {
        using var provider = BuildServices(new Dictionary<string, string?>
        {
            ["OpenAI:ApiKey"] = "key",
            ["OpenAI:Provider"] = LlmProviderNames.Azure,
            ["OpenAI:Endpoint"] = AzureEndpoint,
        });

        Assert.NotNull(provider.GetRequiredService<IChatClient>());
        Assert.NotNull(provider.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>());
    }

    [Fact]
    public void A_consumer_resolves_the_chat_client_on_the_unchanged_openai_path()
    {
        using var provider = BuildServices(new Dictionary<string, string?>
        {
            ["OpenAI:ApiKey"] = "key",
        });

        Assert.NotNull(provider.GetRequiredService<IChatClient>());
    }

    [Fact]
    public void Invalid_provider_configuration_stops_the_container_being_built()
    {
        var error = Assert.Throws<InvalidOperationException>(() => BuildServices(
            new Dictionary<string, string?>
            {
                ["OpenAI:ApiKey"] = "key",
                ["OpenAI:Provider"] = LlmProviderNames.Azure,
            }));

        Assert.Contains("OpenAI__Endpoint", error.Message);
    }

    private static ResolvedLlmProvider Resolve(OpenAISettings settings) =>
        LlmProviderResolver.Resolve(settings);

    private static ServiceProvider BuildServices(Dictionary<string, string?> settings)
    {
        // A connection string is required for the container to be built, but it is
        // never dialled: UseNpgsql only configures, so these tests need no database.
        settings["ConnectionStrings:Default"] = "Host=localhost;Database=none;Username=none;Password=none";

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddInfrastructure(configuration, NullLogger.Instance);

        return services.BuildServiceProvider();
    }
}
