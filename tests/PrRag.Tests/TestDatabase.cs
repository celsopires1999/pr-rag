using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PrRag.Infrastructure.Persistence;

namespace PrRag.Tests;

internal static class TestDatabase
{
    private const string LocalHostTemplate = "Host=localhost;Port=5432;Username=prrag;Password=prrag";
    private const string DevContainerHostTemplate = "Host=db;Port=5432;Username=prrag;Password=prrag";

    /// <summary>
    /// Returns the connection-string template (without a database) to use for
    /// integration tests. Honors TEST_CONNECTION_STRING when set; otherwise falls
    /// back to the Postgres host that is reachable from the current environment.
    /// When running inside the DevContainer (VS Code Dev Containers) the database
    /// is the compose "db" service, reachable on host "db", not localhost.
    /// </summary>
    public static string ConnectionStringTemplate =>
        Environment.GetEnvironmentVariable("TEST_CONNECTION_STRING")
        ?? (IsInsideDevContainer ? DevContainerHostTemplate : LocalHostTemplate);

    private static bool IsInsideDevContainer =>
        Environment.GetEnvironmentVariable("REMOTE_CONTAINERS")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true
        || Directory.Exists("/.dockerenv")
        || Directory.Exists("/workspaces");

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

    /// <summary>
    /// Drops a test database created for a test run. Connects through the same
    /// reachable-host template used to create the database (TEST_CONNECTION_STRING,
    /// DevContainer "db", or localhost). Uses DROP DATABASE ... WITH (FORCE) so any
    /// pooled idle connections left by Npgsql do not block the drop.
    /// </summary>
    public static async Task DropDatabaseAsync(
        string dbName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(ConnectionStringTemplate);
        await connection.OpenAsync(cancellationToken);

        var quoted = $"\"{dbName.Replace("\"", "\"\"")}\"";
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS {quoted} WITH (FORCE)";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}