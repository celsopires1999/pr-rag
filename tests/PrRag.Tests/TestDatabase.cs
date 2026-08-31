using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PrRag.Infrastructure.Persistence;

namespace PrRag.Tests;

internal static class TestDatabase
{
    /// <summary>
    /// Applies migrations and reloads Npgsql's type info on the EF connection.
    /// Migrating a freshly created database runs CREATE EXTENSION vector; the
    /// Npgsql DatabaseInfo cache for that new catalog may have been populated
    /// before the 'vector' type existed, causing writes of Vector values to fail
    /// with "Cannot resolve 'vector'". Reloading types on a connection from the
    /// same data source refreshes that cache so the resolver can map the type.
    /// </summary>
    public static async Task MigrateAndReloadTypesAsync(
        ServiceProvider provider,
        CancellationToken cancellationToken = default)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PrRagDbContext>();
        await db.Database.MigrateAsync(cancellationToken);

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await connection.ReloadTypesAsync(cancellationToken);
    }
}