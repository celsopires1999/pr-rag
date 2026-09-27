using Npgsql;
using Xunit;

namespace PrRag.Tests;

/// <summary>
/// Guards the create-and-drop lifecycle every other test class in this suite depends
/// on. Deliberately NOT an IAsyncLifetime class with its own migrated database: this
/// needs a *server* to create and drop a catalog on, not a schema, and giving it the
/// usual per-class database would add a thirteenth to the lifecycle it exists to keep
/// honest. It owns nothing but the two catalogs it creates and drops itself.
/// </summary>
/// <remarks>
/// The host-resolution tests call <see cref="TestDatabase.ResolveTemplate"/> directly
/// and this file never calls SetEnvironmentVariable. The suite runs classes in parallel
/// and twelve of them read TEST_CONNECTION_STRING while building their connection
/// strings, so mutating it here would make them fail intermittently for no visible
/// reason.
/// </remarks>
public class TestDatabaseLifecycleTests
{
    private const string Supplied = "Host=elsewhere;Port=6543;Username=someone;Password=secret";
    private const string ContainerHost = "Host=db;";
    private const string LocalHost = "Host=localhost;";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_supplied_connection_string_wins_outright(bool isInsideDevContainer)
    {
        Assert.Equal(Supplied, TestDatabase.ResolveTemplate(Supplied, isInsideDevContainer));
    }

    [Fact]
    public void Without_a_supplied_string_container_detection_selects_the_db_service()
    {
        Assert.StartsWith(ContainerHost, TestDatabase.ResolveTemplate(null, isInsideDevContainer: true));
    }

    [Fact]
    public void Without_a_supplied_string_outside_a_container_it_falls_back_to_localhost()
    {
        Assert.StartsWith(LocalHost, TestDatabase.ResolveTemplate(null, isInsideDevContainer: false));
    }

    [Fact]
    public async Task Dropping_a_database_removes_it_from_the_server()
    {
        var dbName = NewDbName();
        await TestDatabase.CreateDatabaseAsync(dbName);
        Assert.True(await ExistsAsync(dbName), "CREATE DATABASE should have registered a catalog to drop.");

        await TestDatabase.DropDatabaseAsync(dbName);

        Assert.False(await ExistsAsync(dbName), $"'{dbName}' is still in pg_database after being dropped.");
    }

    [Fact]
    public async Task Dropping_an_already_absent_database_completes_without_error()
    {
        var dbName = NewDbName();

        // The other half of IF EXISTS, and the assertion more likely to be dropped in a
        // refactor because a redundant-looking drop reads as dead code. It is the only
        // thing between a typo in the clause and teardown throwing on every clean database.
        await TestDatabase.DropDatabaseAsync(dbName);
        await TestDatabase.DropDatabaseAsync(dbName);

        Assert.False(await ExistsAsync(dbName));
    }

    private static string NewDbName() => $"prrag_test_{Guid.NewGuid():N}";

    private static async Task<bool> ExistsAsync(string dbName)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionStringTemplate);
        await connection.OpenAsync();

        // Queried over the same resolved template the drop used, so a wrong-host
        // regression fails here rather than passing against a different server.
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @name)";
        command.Parameters.AddWithValue("name", dbName);
        return (bool)(await command.ExecuteScalarAsync())!;
    }
}
