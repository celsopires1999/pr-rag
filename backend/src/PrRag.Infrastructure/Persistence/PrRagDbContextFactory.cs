using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pgvector.Npgsql;

namespace PrRag.Infrastructure.Persistence;

/// <summary>
/// Design-time factory so `dotnet ef` can build the model without a live app
/// host (Program.cs requires config keys that are absent in a bare shell).
/// Migrations are generated offline, so the connection string is only a stub.
/// </summary>
public sealed class PrRagDbContextFactory : IDesignTimeDbContextFactory<PrRagDbContext>
{
    public PrRagDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PrRagDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=prrag;Username=prrag;Password=prrag",
                npgsql => npgsql.UseVector())
            .Options;

        return new PrRagDbContext(options);
    }
}