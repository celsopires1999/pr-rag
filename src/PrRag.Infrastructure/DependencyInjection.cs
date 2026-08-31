using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenAI.Chat;
using OpenAI.Embeddings;
using PrRag.Application.Abstractions;
using PrRag.Application.Configuration;
using PrRag.Infrastructure.Embeddings;
using PrRag.Infrastructure.Persistence;
using PrRag.Infrastructure.Services;

namespace PrRag.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<OpenAISettings>(configuration.GetSection(OpenAISettings.SectionName));
        services.Configure<RagSettings>(configuration.GetSection(RagSettings.SectionName));
        services.Configure<DataSettings>(configuration.GetSection(DataSettings.SectionName));

        var openAi = configuration.GetSection(OpenAISettings.SectionName).Get<OpenAISettings>()
            ?? new OpenAISettings();

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Connection string 'Default' is not configured. Set ConnectionStrings__Default.");

        services.AddDbContext<PrRagDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.UseVector()));

        var chatClient = new ChatClient(openAi.ChatModel, openAi.ApiKey);
        var embeddingClient = new EmbeddingClient(openAi.EmbeddingModel, openAi.ApiKey);

        services.AddChatClient(_ => chatClient.AsIChatClient())
            .UseLogging();

        services.AddEmbeddingGenerator(_ => embeddingClient.AsIEmbeddingGenerator())
            .UseLogging();

        services.AddScoped<IPurchaseRequisitionRepository, PurchaseRequisitionRepository>();
        services.AddScoped<IEmbeddingService, OpenAiEmbeddingService>();
        services.AddScoped<IQueryRewriter, OpenAiQueryRewriter>();

        services.AddHostedService<FileWatcherService>();

        return services;
    }
}
