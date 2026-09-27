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
        ResolveTemplate(Environment.GetEnvironmentVariable("TEST_CONNECTION_STRING"), IsInsideDevContainer);

    /// <summary>
    /// The host-resolution decision, extracted from its inputs so every branch is
    /// reachable by a test without mutating process-global state. The suite runs
    /// classes in parallel and twelve of them read TEST_CONNECTION_STRING to build
    /// their connection strings, so a test that set the variable to reach the
    /// override branch would break them intermittently for no visible reason.
    /// </summary>
    public static string ResolveTemplate(string? testConnectionString, bool isInsideDevContainer) =>
        testConnectionString
        ?? (isInsideDevContainer ? DevContainerHostTemplate : LocalHostTemplate);

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
    /// Creates a test database explicitly. The twelve integration classes get theirs
    /// implicitly, as a side effect of EF's MigrateAsync, which is the wrong mechanism
    /// when the subject is teardown: it pays for migrations and a seeded schema to
    /// obtain a catalog whose only purpose is to be dropped. Dropping something that
    /// was never created asserts nothing, so the create half is explicit and symmetric
    /// with <see cref="DropDatabaseAsync"/>.
    /// </summary>
    public static async Task CreateDatabaseAsync(
        string dbName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(ConnectionStringTemplate);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE {Quote(dbName)}";
        await command.ExecuteNonQueryAsync(cancellationToken);
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

        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS {Quote(dbName)} WITH (FORCE)";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Quote(string dbName) => $"\"{dbName.Replace("\"", "\"\"")}\"";
}