using Microsoft.Extensions.DependencyInjection;
using PrRag.Application.Abstractions;
using PrRag.Application.Services;

namespace PrRag.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<PurchaseRequisitionFileLoader>();
        services.AddScoped<IIngestionService, IngestionService>();
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IStatusService, StatusService>();
        services.AddScoped<IQueryRewriter, SemanticQueryRewriter>();

        return services;
    }
}
