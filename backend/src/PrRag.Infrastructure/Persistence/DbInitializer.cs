using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PrRag.Infrastructure.Persistence;

public static class DbInitializer
{
    public static async Task ApplyMigrationsAsync(
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PrRagDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<PrRagDbContext>>();

        await dbContext.Database.MigrateAsync(cancellationToken);

        logger.LogInformation("Database migrations applied.");
    }
}
