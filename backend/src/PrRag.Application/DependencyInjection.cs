using Microsoft.Extensions.DependencyInjection;
using PrRag.Application.Abstractions;
using PrRag.Application.Services;
using PrRag.Application.Services.Agents;
using PrRag.Application.Services.Agents.Specialists;

namespace PrRag.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<PurchaseRequisitionFileLoader>();
        services.AddSingleton<IAgentSessionStore, InMemoryAgentSessionStore>();
        services.AddScoped<AgentTurnContext>();
        services.AddScoped<SpecialistToolSet>();
        services.AddScoped<RequisitionSearchSpecialist>();
        services.AddScoped<RequisitionCreationSpecialist>();
        services.AddScoped<SkillActivationSpecialist>();
        services.AddScoped<ISpecialistCatalog, SpecialistCatalog>();
        // Scoped for the same reason the specialists are: the composer captures
        // the request-scoped AgentTurnContext through the tool handlers it binds,
        // so the graph must be rebuilt per request rather than shared.
        services.AddScoped<AgentGraphComposer>();
        services.AddScoped<IAgentRunService, AgentRunService>();
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IIngestionService, IngestionService>();
        services.AddScoped<IStatusService, StatusService>();

        return services;
    }
}
