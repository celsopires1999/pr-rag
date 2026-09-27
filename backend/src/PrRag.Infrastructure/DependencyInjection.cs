using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PrRag.Application.Abstractions;
using PrRag.Application.Configuration;
using PrRag.Infrastructure.Embeddings;
using PrRag.Infrastructure.Persistence;
using PrRag.Infrastructure.Providers;
using PrRag.Infrastructure.Services;

namespace PrRag.Infrastructure;

public static class DependencyInjection
{
    /// <param name="logger">
    /// A bootstrap logger, because provider resolution happens while the container
    /// is still being built and no <c>ILogger</c> is resolvable yet. The resolved
    /// provider is logged here rather than from a hosted service so that the log
    /// sits beside the validation that produced it.
    /// </param>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        ILogger logger)
    {
        services.Configure<OpenAISettings>(configuration.GetSection(OpenAISettings.SectionName));
        services.Configure<RagSettings>(configuration.GetSection(RagSettings.SectionName));
        services.Configure<DataSettings>(configuration.GetSection(DataSettings.SectionName));
        services.Configure<ReportSettings>(configuration.GetSection(ReportSettings.SectionName));
        services.Configure<SkillsSettings>(configuration.GetSection(SkillsSettings.SectionName));

        var openAi = configuration.GetSection(OpenAISettings.SectionName).Get<OpenAISettings>()
            ?? new OpenAISettings();

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Connection string 'Default' is not configured. Set ConnectionStrings__Default.");

        services.AddDbContext<PrRagDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.UseVector()));

        var provider = LlmProviderResolver.Resolve(openAi);
        var (chatClient, embeddingClient) = LlmClientFactory.Create(provider);

        services.AddChatClient(_ => chatClient)
            .UseLogging();

        services.AddEmbeddingGenerator(_ => embeddingClient)
            .UseLogging();

        // The provider and both deployment names are logged because nothing else
        // records them: the report, the status endpoint, and the chat log lines
        // cannot answer which model served a request, and on Azure the model
        // settings are deployment names whose correctness is not checkable offline.
        logger.LogInformation(
            "LLM provider resolved: provider={Provider} endpoint={Endpoint} chat={Chat} embeddings={Embeddings}",
            provider.Provider,
            provider.Endpoint?.ToString() ?? "(default)",
            provider.ChatModel,
            provider.EmbeddingModel);

        services.AddScoped<IPurchaseRequisitionRepository, PurchaseRequisitionRepository>();
        services.AddScoped<ICreatedRequisitionQuery, CreatedRequisitionQuery>();
        services.AddScoped<IEmbeddingService, OpenAiEmbeddingService>();
        services.AddSingleton<IRagReportWriter, FileRagReportWriter>();
        services.AddSingleton<ISkillService, SkillsDirectory>();
        services.AddScoped<IRequisitionWriter, DbRequisitionWriter>();

        services.AddHostedService<FileWatcherService>();
        services.AddHostedService<SkillsWatcherService>();

        return services;
    }
}
