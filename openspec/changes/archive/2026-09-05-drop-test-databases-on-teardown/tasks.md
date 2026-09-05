## 1. Add drop helper to TestDatabase

- [x] 1.1 Add `DropDatabaseAsync(string dbName, CancellationToken cancellationToken = default)` to `tests/PrRag.Tests/TestDatabase.cs` that opens an `NpgsqlConnection` with `ConnectionStringTemplate`, quotes the identifier defensively, and executes `DROP DATABASE IF EXISTS "<dbName>" WITH (FORCE)`

## 2. Wire teardown into integration test classes

- [x] 2.1 In `IngestionDiffTests.DisposeAsync`, after `_provider.DisposeAsync()`, call `await TestDatabase.DropDatabaseAsync(_dbName);`
- [x] 2.2 In `AgenticRetrievalTests.DisposeAsync`, after `_provider.DisposeAsync()`, call `await TestDatabase.DropDatabaseAsync(_dbName);`
- [x] 2.3 In `RagObservabilityReportTests.DisposeAsync`, after `_provider.DisposeAsync()`, call `await TestDatabase.DropDatabaseAsync(_dbName);`

## 3. Verify

- [x] 3.1 Run the suite against the compose `db` (`docker compose exec -u vscode -w /workspaces devcontainer sh -c 'TEST_CONNECTION_STRING="Host=db;Port=5432;Username=prrag;Password=prrag" dotnet test tests/PrRag.Tests'`) and confirm all tests pass
- [x] 3.2 Confirm segment: run the full suite, then check `SELECT count(*) FROM pg_database WHERE datname LIKE 'prrag_test_%'` in `prrag-db`; count of test databases created by this run is zero (pre-existing orphans may still be present)