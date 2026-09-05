using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector.Npgsql;
using PrRag.Application;
using PrRag.Application.Abstractions;
using PrRag.Application.Configuration;
using PrRag.Infrastructure.Persistence;
using PrRag.Infrastructure.Services;

namespace PrRag.Tests;

public static class IntegrationServiceFactory
{
    public static (
        ServiceProvider Provider,
        FakeEmbeddingService Embeddings,
        string DataDir) Create(
        string connectionString)
    {
        var dataDir = Path.Combine(Path.GetTempPath(), $"prrag-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataDir);

        var config = new ConfigurationBuilder().Build();

        var embeddingService = new FakeEmbeddingService();
        var chatClient = new FakeChatClient();
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.Configure<DataSettings>(opts => opts.FilePath = Path.Combine(dataDir, "purchase.json"));
        services.Configure<RagSettings>(opts => { });
        services.Configure<OpenAISettings>(opts => { });
        services.Configure<ReportSettings>(opts => opts.OutputDirectory = Path.Combine(dataDir, "reports"));
        services.AddSingleton<IRagReportWriter, FileRagReportWriter>();

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
#if NET8_0_OR_GREATER
#pragma warning disable NPG9001 // evaluation-only API; required for pgvector type mapping
        dataSourceBuilder.AddTypeInfoResolverFactory(new VectorTypeInfoResolverFactory());
#pragma warning restore NPG9001
#endif
        var dataSource = dataSourceBuilder.Build();

        services.AddDbContext<PrRagDbContext>(options =>
            options.UseNpgsql(dataSource, npgsql => npgsql.UseVector()));

        services.AddApplication();

        services.AddSingleton<IEmbeddingService>(embeddingService);
        services.AddSingleton(chatClient);
        services.AddSingleton<IChatClient>(chatClient);
        services.AddScoped<IPurchaseRequisitionRepository, PurchaseRequisitionRepository>();

        var provider = services.BuildServiceProvider();
        return (provider, embeddingService, dataDir);
    }
}
