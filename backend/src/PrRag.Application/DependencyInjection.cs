using Microsoft.Extensions.DependencyInjection;
using PrRag.Application.Abstractions;
using PrRag.Application.Services;
using PrRag.Application.Services.Agents;

namespace PrRag.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<PurchaseRequisitionFileLoader>();
        services.AddSingleton<IAgentSessionStore, InMemoryAgentSessionStore>();
        services.AddScoped<AgentTurnContext>();
        services.AddScoped<PurchaseRequisitionTools>();
        services.AddScoped<IAgentRunService, AgentRunService>();
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IIngestionService, IngestionService>();
        services.AddScoped<IStatusService, StatusService>();

        return services;
    }
}
