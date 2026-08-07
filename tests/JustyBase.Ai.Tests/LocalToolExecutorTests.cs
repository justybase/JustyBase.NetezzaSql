using JustyBase.Ai.Ports;
using JustyBase.Ai.Services;

namespace JustyBase.Ai.Tests;

/// <summary>LocalToolExecutor schema tools, diagnostics and SQL write tools against fake ports.</summary>
public sealed class LocalToolExecutorTests
{
    private static LocalToolExecutor CreateExecutor(
        ChatApprovalGateTests.FakeDbAccess dbAccess,
        ISqlDiagnosticsProvider? diagnostics = null)
    {
        var executor = new LocalToolExecutor(
            EmptySimpleLogger.Instance,
            new ChatApprovalGateTests.TestDatabaseAccessProvider(dbAccess),
            diagnostics ?? EmptySqlDiagnosticsProvider.Instance,
            new SqlExecutionErrorStore(),
            new ChatApprovalGateTests.TestDispatcher());
        executor.SetActiveSqlContextProvider(() => ("conn1", "db1"));
        return executor;
    }

    [Fact]
    public void GetActiveDatabaseContext_ListsConnectionDatabaseAndSchemas()
    {
        var executor = CreateExecutor(new ChatApprovalGateTests.FakeDbAccess());

        var result = executor.GetActiveDatabaseContext();

        Assert.Contains("conn1", result);
        Assert.Contains("db1", result);
        Assert.Contains("db1.ADMIN", result);
        Assert.Contains("db1.PUBLIC", result);
    }

    [Fact]
    public void ListSchemas_ReturnsSchemas()
    {
        var executor = CreateExecutor(new ChatApprovalGateTests.FakeDbAccess());

        var result = executor.ListSchemas();

        Assert.Contains("db1.ADMIN", result);
        Assert.Contains("db1.PUBLIC", result);
    }

    [Fact]
    public void BrowseSchemaObjects_ListsTableObjects()
    {
        var executor = CreateExecutor(new ChatApprovalGateTests.FakeDbAccess());

        var result = executor.BrowseSchemaObjects("PUBLIC", "table");

        Assert.Contains("db1.PUBLIC.MY_TABLE", result);
    }

    [Fact]
    public void GetObjectColumns_ReturnsTypedColumns()
    {
        var executor = CreateExecutor(new ChatApprovalGateTests.FakeDbAccess());

        var result = executor.GetObjectColumns("MY_TABLE", "PUBLIC", "db1");

        Assert.Contains("ID INTEGER", result);
    }

    [Fact]
    public async Task GetObjectDefinition_ReturnsDdl()
    {
        var executor = CreateExecutor(new ChatApprovalGateTests.FakeDbAccess());

        var result = await executor.GetObjectDefinition("db1.PUBLIC.MY_TABLE", "table");

        Assert.Contains("CREATE TABLE", result);
        Assert.Contains("db1.PUBLIC.MY_TABLE", result);
    }

    [Fact]
    public async Task GetTableMetadata_IncludesDistributionFromDdlAndPort()
    {
        var executor = CreateExecutor(new ChatApprovalGateTests.FakeDbAccess());

        var result = await executor.GetTableMetadata("db1.PUBLIC.MY_TABLE");

        Assert.Contains("DISTRIBUTE ON", result);
        Assert.Contains("Distribution columns: ID", result);
    }

    [Fact]
    public async Task GetDiagnostics_FormatsItemsFromProvider()
    {
        var diagnostics = new StaticDiagnosticsProvider(
        [
            new ChatDiagnosticItem("NZ001", "Syntax error", "Error", 3, 5),
            new ChatDiagnosticItem("NZ010", "Hint", "Hint", 0, 0),
        ]);
        var executor = CreateExecutor(new ChatApprovalGateTests.FakeDbAccess(), diagnostics);

        var result = await executor.GetDiagnostics();

        Assert.Contains("[Error] NZ001: Syntax error L3:5", result);
        Assert.Contains("[Hint] NZ010: Hint", result);
    }

    [Fact]
    public async Task GetDiagnostics_Empty_ReturnsNoIssues()
    {
        var executor = CreateExecutor(new ChatApprovalGateTests.FakeDbAccess());

        var result = await executor.GetDiagnostics();

        Assert.Contains("No diagnostics issues found", result);
    }

    [Fact]
    public async Task GetLastExecutionError_Empty_ReturnsNoError()
    {
        var executor = CreateExecutor(new ChatApprovalGateTests.FakeDbAccess());

        var result = await executor.GetLastExecutionError();

        Assert.Equal("No SQL execution error has been recorded.", result);
    }

    [Fact]
    public async Task ExecuteSql_RunsThroughDatabasePort()
    {
        var dbAccess = new ChatApprovalGateTests.FakeDbAccess();
        var executor = CreateExecutor(dbAccess);

        var result = await executor.ExecuteSql("DELETE FROM t");

        Assert.Contains("Affected rows: 3", result);
        Assert.Equal("DELETE FROM t", dbAccess.LastExecutedSql);
    }

    [Fact]
    public async Task ApplySqlFix_UpdatesEditorBuffer()
    {
        var executor = CreateExecutor(new ChatApprovalGateTests.FakeDbAccess());
        executor.SetCurrentSqlProvider(() => "SELECT 1");
        string? applied = null;
        executor.SetSqlEditorBufferUpdater(sql => { applied = sql; return true; });

        var result = await executor.ApplySqlFix("SELECT 2");

        Assert.Contains("Patch applied", result);
        Assert.Equal("SELECT 2", applied);
    }

    [Fact]
    public async Task GetCurrentSql_ReturnsEditorSql()
    {
        var executor = CreateExecutor(new ChatApprovalGateTests.FakeDbAccess());
        executor.SetCurrentSqlProvider(() => "SELECT * FROM t");

        var result = await executor.GetCurrentSql();

        Assert.Equal("SELECT * FROM t", result);
    }

    private sealed class StaticDiagnosticsProvider : ISqlDiagnosticsProvider
    {
        public StaticDiagnosticsProvider(IReadOnlyList<ChatDiagnosticItem> items) => Items = items;
        public IReadOnlyList<ChatDiagnosticItem> Items { get; }
    }
}
