using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PrRag.Application;
using PrRag.Application.Abstractions;
using PrRag.Application.Configuration;
using PrRag.Infrastructure.Persistence;
using PrRag.Infrastructure.Services;

namespace PrRag.Tests;

public static class IntegrationServiceFactory
{
    public static (ServiceProvider Provider, FakeEmbeddingService Embeddings, string DataDir) Create(
        string connectionString)
    {
        var dataDir = Path.Combine(Path.GetTempPath(), $"prrag-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataDir);

        var config = new ConfigurationBuilder().Build();

        var embeddingService = new FakeEmbeddingService();
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.Configure<DataSettings>(opts => opts.FilePath = Path.Combine(dataDir, "purchase.json"));
        services.Configure<RagSettings>(opts => { });
        services.Configure<OpenAISettings>(opts => { });

        services.AddDbContext<PrRagDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.UseVector()));

        services.AddSingleton<IEmbeddingService>(embeddingService);
        services.AddScoped<IPurchaseRequisitionRepository, PurchaseRequisitionRepository>();
        services.AddApplication();

        var provider = services.BuildServiceProvider();
        return (provider, embeddingService, dataDir);
    }
}
