using JustyBase.Netezza.Models;
using JustyBase.Netezza.Schema;

namespace JustyBase.Netezza.Tests;

public sealed class NetezzaSchemaLoaderTests
{
    private static object?[] Obj(int id, string name, string? desc, string schema, string type, string? owner = "DBA", DateTime? created = null)
        => [id, name, desc, schema, type, owner, created];

    private static object?[] Col(int objId, string name, string? desc, string type, object notNull, string? defaultValue = null)
        => [objId, name, desc, type, notNull, defaultValue];

    private static object?[] Db(int id, int defSchemaId, string name, string? owner, string defSchema)
        => [id, defSchemaId, name, owner, defSchema];

    private static object?[] Proc(string schema, string source, int objId, string returns, object execAsOwner, string? desc, string signature, string? arguments, string? language)
        => [schema, source, objId, returns, execAsOwner, desc, signature, arguments, language];

    [Fact]
    public async Task LoadDatabasesAsync_MapsRows()
    {
        using var connection = new FakeCatalogConnection(
            databaseRows:
            [
                Db(1, 10, "SALES", "owner1", "PUBLIC"),
                Db(2, 11, "SYSTEM", null, "ADMIN"),
            ]);

        var databases = await NetezzaSchemaLoader.LoadDatabasesAsync(connection);

        Assert.Equal(2, databases.Count);
        Assert.Equal("SALES", databases[0].Name);
        Assert.Equal("PUBLIC", databases[0].DefaultSchema);
        Assert.Equal("owner1", databases[0].Owner);
        Assert.Equal(1, databases[0].CatalogId);
        Assert.Equal("SYSTEM", databases[1].Name);
        Assert.Null(databases[1].Owner);
    }

    [Fact]
    public async Task LoadCatalogAsync_MapsAllObjectKinds()
    {
        using var connection = new FakeCatalogConnection(
            objectRows:
            [
                Obj(1, "CUSTOMERS", "main table", "PUBLIC", "TABLE"),
                Obj(2, "ACTIVE_CUSTOMERS", null, "PUBLIC", "VIEW"),
                Obj(3, "GET_REPORT", null, "PUBLIC", "PROCEDURE"),
                Obj(4, "SCORE_CALC", null, "PUBLIC", "FUNCTION"),
                Obj(5, "SEQ_1", null, "PUBLIC", "SEQUENCE"),
                Obj(6, "SYN_CUSTOMERS", null, "PUBLIC", "SYNONYM"),
                Obj(7, "EXT_EVENTS", "remote", "PUBLIC", "EXTERNAL TABLE"),
                Obj(8, "FLUID_PROC", null, "PUBLIC", "FLUID"),
                Obj(9, "AGG_1", null, "PUBLIC", "AGGREGATE"),
                Obj(10, "IDX_1", null, "PUBLIC", "INDEX"),
                Obj(11, "PTN_1", null, "PUBLIC", "PARTITION"),
                Obj(12, "WEIRD_1", null, "PUBLIC", "SOMETHING ELSE"),
                Obj(13, "DETACHED_1", null, "PUBLIC", "DETACHED TABLE"),
            ]);

        var snapshot = await NetezzaSchemaLoader.LoadCatalogAsync(connection, "SALES");

        var kinds = snapshot.Tables.ToDictionary(t => t.Name, t => t.Kind);
        Assert.Equal(NetezzaObjectKind.Table, kinds["CUSTOMERS"]);
        Assert.Equal(NetezzaObjectKind.View, kinds["ACTIVE_CUSTOMERS"]);
        Assert.Equal(NetezzaObjectKind.Procedure, kinds["GET_REPORT"]);
        Assert.Equal(NetezzaObjectKind.Function, kinds["SCORE_CALC"]);
        Assert.Equal(NetezzaObjectKind.Sequence, kinds["SEQ_1"]);
        Assert.Equal(NetezzaObjectKind.Synonym, kinds["SYN_CUSTOMERS"]);
        Assert.Equal(NetezzaObjectKind.ExternalTable, kinds["EXT_EVENTS"]);
        Assert.Equal(NetezzaObjectKind.Fluid, kinds["FLUID_PROC"]);
        Assert.Equal(NetezzaObjectKind.Aggregate, kinds["AGG_1"]);
        Assert.Equal(NetezzaObjectKind.Index, kinds["IDX_1"]);
        Assert.Equal(NetezzaObjectKind.Partition, kinds["PTN_1"]);
        Assert.Equal(NetezzaObjectKind.Other, kinds["WEIRD_1"]);
        Assert.Equal(NetezzaObjectKind.Table, kinds["DETACHED_1"]);

        Assert.True(snapshot.Tables.First(t => t.Name == "ACTIVE_CUSTOMERS").IsView);
        Assert.False(snapshot.Tables.First(t => t.Name == "CUSTOMERS").IsView);
        Assert.Equal("main table", snapshot.Tables.First(t => t.Name == "CUSTOMERS").Description);
        Assert.Equal("DBA", snapshot.Tables.First(t => t.Name == "CUSTOMERS").Owner);
        Assert.Equal(1, snapshot.Tables.First(t => t.Name == "CUSTOMERS").CatalogId);
        Assert.Equal("SALES", snapshot.Tables.First(t => t.Name == "CUSTOMERS").Database);
    }

    [Fact]
    public async Task LoadCatalogAsync_AttachesColumnsEagerly()
    {
        using var connection = new FakeCatalogConnection(
            objectRows:
            [
                Obj(1, "CUSTOMERS", null, "PUBLIC", "TABLE"),
                Obj(2, "ORDERS", null, "PUBLIC", "TABLE"),
            ],
            columnRows:
            [
                Col(1, "ID", null, "INTEGER", true),
                Col(1, "NAME", "display name", "VARCHAR(100)", false, "''"),
                Col(1, "PRICE", null, "DECIMAL(10,2)", false, null),
                Col(99, "ORPHAN", null, "INTEGER", false),
            ]);

        var snapshot = await NetezzaSchemaLoader.LoadCatalogAsync(connection, "SALES");

        var customers = snapshot.Tables.First(t => t.Name == "CUSTOMERS");
        Assert.NotNull(customers.Columns);
        Assert.Equal(3, customers.Columns!.Count);
        Assert.Equal("ID", customers.Columns[0].Name);
        Assert.Equal("INTEGER", customers.Columns[0].DataType);
        Assert.False(customers.Columns[0].Nullable);
        Assert.Equal("display name", customers.Columns[1].Description);
        Assert.Equal("''", customers.Columns[1].DefaultValue);
        Assert.True(customers.Columns[2].Nullable);
        Assert.Equal("DECIMAL(10,2)", customers.Columns[2].DataType);

        var orders = snapshot.Tables.First(t => t.Name == "ORDERS");
        Assert.Null(orders.Columns);
    }

    [Fact]
    public async Task LoadCatalogAsync_NotNullFlagAcceptsBoolAndInt()
    {
        using var connection = new FakeCatalogConnection(
            objectRows: [Obj(1, "T1", null, "PUBLIC", "TABLE")],
            columnRows:
            [
                Col(1, "A", null, "INTEGER", true),
                Col(1, "B", null, "INTEGER", 1),
                Col(1, "C", null, "INTEGER", false),
                Col(1, "D", null, "INTEGER", 0),
            ]);

        var snapshot = await NetezzaSchemaLoader.LoadCatalogAsync(connection, "SALES");
        var columns = snapshot.Tables.Single().Columns!;

        Assert.False(columns[0].Nullable);
        Assert.False(columns[1].Nullable);
        Assert.True(columns[2].Nullable);
        Assert.True(columns[3].Nullable);
    }

    [Fact]
    public async Task LoadCatalogAsync_DBNullOptionalFieldsBecomeNull()
    {
        using var connection = new FakeCatalogConnection(
            objectRows:
            [
                [11, "T1", DBNull.Value, "PUBLIC", "TABLE", DBNull.Value, DBNull.Value],
            ]);

        var snapshot = await NetezzaSchemaLoader.LoadCatalogAsync(connection, "SALES");

        var table = snapshot.Tables.Single();
        Assert.Null(table.Description);
        Assert.Null(table.Owner);
        Assert.Equal(11, table.CatalogId);
    }

    [Fact]
    public async Task LoadCatalogAsync_DedupsObjectNamesCaseInsensitively()
    {
        using var connection = new FakeCatalogConnection(
            objectRows:
            [
                Obj(1, "EMP", null, "PUBLIC", "TABLE"),
                Obj(2, "emp", null, "PUBLIC", "TABLE"),
                Obj(3, "EMP", null, "ADMIN", "TABLE"),
            ]);

        var snapshot = await NetezzaSchemaLoader.LoadCatalogAsync(connection, "SALES");

        Assert.Equal(2, snapshot.Tables.Count);
        Assert.Contains(snapshot.Tables, t => t.Name == "EMP" && t.Schema == "PUBLIC");
        Assert.Contains(snapshot.Tables, t => t.Name == "EMP" && t.Schema == "ADMIN");
    }

    [Fact]
    public async Task LoadCatalogAsync_DeferredColumnsForLargeCatalogs()
    {
        var objects = new List<object?[]>();
        var columns = new List<object?[]>();
        for (int i = 0; i < 500; i++)
        {
            objects.Add(Obj(i + 1, $"T{i}", null, "PUBLIC", "TABLE"));
            columns.Add(Col(i + 1, "ID", null, "INTEGER", false));
        }

        using var connection = new FakeCatalogConnection(objects, columns);

        var snapshot = await NetezzaSchemaLoader.LoadCatalogAsync(connection, "SALES");

        Assert.Equal(500, snapshot.Tables.Count);
        Assert.All(snapshot.Tables, t => Assert.Null(t.Columns));

        using var hydrateConnection = new FakeCatalogConnection(columnRows: [Col(1, "ID", null, "INTEGER", false)]);
        var hydrated = await NetezzaSchemaLoader.HydrateColumnsAsync(hydrateConnection, "SALES", "PUBLIC", "T0");
        Assert.Single(hydrated);
        Assert.Equal("ID", hydrated[0].Name);
    }

    [Fact]
    public async Task LoadCatalogAsync_EagerColumnsBelowThreshold()
    {
        var objects = new List<object?[]>();
        var columns = new List<object?[]>();
        for (int i = 0; i < 499; i++)
        {
            objects.Add(Obj(i + 1, $"T{i}", null, "PUBLIC", "TABLE"));
            columns.Add(Col(i + 1, "ID", null, "INTEGER", false));
        }

        using var connection = new FakeCatalogConnection(objects, columns);

        var snapshot = await NetezzaSchemaLoader.LoadCatalogAsync(connection, "SALES");

        Assert.Equal(499, snapshot.Tables.Count);
        Assert.All(snapshot.Tables, t => Assert.NotNull(t.Columns));
    }

    [Fact]
    public async Task LoadCatalogAsync_LoadsProcedures()
    {
        using var connection = new FakeCatalogConnection(
            objectRows: [Obj(1, "P1", null, "PUBLIC", "PROCEDURE")],
            procedureRows:
            [
                Proc("PUBLIC", "CREATE PROCEDURE P1() ...", 1, "void", true, "does things", "P1", "(X INTEGER)", null),
                Proc("PUBLIC", "CREATE PROCEDURE P2() ...", 2, "integer", 0, null, "P2", null, "NZPLSQL"),
            ]);

        var snapshot = await NetezzaSchemaLoader.LoadCatalogAsync(connection, "SALES");

        Assert.NotNull(snapshot.Procedures);
        Assert.Equal(2, snapshot.Procedures!.Count);
        Assert.Equal("SALES", snapshot.Procedures[0].Database);
        Assert.Equal("PUBLIC", snapshot.Procedures[0].Schema);
        Assert.Equal("P1", snapshot.Procedures[0].Name);
        Assert.True(snapshot.Procedures[0].ExecuteAsOwner);
        Assert.Equal("does things", snapshot.Procedures[0].Description);
        Assert.Equal("(X INTEGER)", snapshot.Procedures[0].Arguments);
        Assert.False(snapshot.Procedures[1].ExecuteAsOwner);
    }

    [Fact]
    public async Task LoadCatalogAsync_NoProceduresWhenDisabled()
    {
        using var connection = new FakeCatalogConnection(objectRows: [Obj(1, "T1", null, "PUBLIC", "TABLE")]);

        var snapshot = await NetezzaSchemaLoader.LoadCatalogAsync(
            connection,
            "SALES",
            new NetezzaCatalogLoadOptions { LoadProcedures = false });

        Assert.Null(snapshot.Procedures);
    }

    [Fact]
    public async Task LoadAllAsync_IsolatesFailingDatabase()
    {
        using var connection = new FakeCatalogConnection(
            databaseRows:
            [
                Db(1, 10, "GOOD_DB", null, "PUBLIC"),
                Db(2, 11, "BAD_DB", null, "PUBLIC"),
            ],
            objectRows: [Obj(1, "T1", null, "PUBLIC", "TABLE")],
            failMarker: "BAD_DB");

        var results = await NetezzaSchemaLoader.LoadAllAsync(connection);

        var good = results.Single(r => r.Database == "GOOD_DB");
        Assert.Single(good.Snapshot.Tables);
        Assert.False(good.Snapshot.IsPartial);

        var bad = results.Single(r => r.Database == "BAD_DB");
        Assert.Empty(bad.Snapshot.Tables);
        Assert.True(bad.Snapshot.IsPartial);
    }

    [Fact]
    public async Task LoadAllAsync_ThrowsWhenFailOnDatabaseError()
    {
        using var connection = new FakeCatalogConnection(
            databaseRows: [Db(1, 10, "BAD_DB", null, "PUBLIC")],
            objectRows: [Obj(1, "T1", null, "PUBLIC", "TABLE")],
            failMarker: "BAD_DB");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NetezzaSchemaLoader.LoadAllAsync(
                connection,
                new NetezzaCatalogLoadOptions { FailOnDatabaseError = true }));
    }

    [Fact]
    public async Task LoadAllAsync_CancellationPropagates()
    {
        using var connection = new FakeCatalogConnection(
            databaseRows: [Db(1, 10, "DB1", null, "PUBLIC")],
            objectRows: [Obj(1, "T1", null, "PUBLIC", "TABLE")]);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            NetezzaSchemaLoader.LoadAllAsync(connection, new NetezzaCatalogLoadOptions(), cts.Token));
    }

    [Fact]
    public async Task LoadCatalogAsync_OpensConnectionWhenClosed()
    {
        using var connection = new FakeCatalogConnection(objectRows: [Obj(1, "T1", null, "PUBLIC", "TABLE")]);

        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        var snapshot = await NetezzaSchemaLoader.LoadCatalogAsync(connection, "SALES");

        Assert.Equal(System.Data.ConnectionState.Open, connection.State);
        Assert.Single(snapshot.Tables);
    }

    [Fact]
    public async Task HydrateColumnsAsync_MapsSingleTable()
    {
        using var connection = new FakeCatalogConnection(
            columnRows:
            [
                Col(1, "A", null, "INTEGER", false),
                Col(1, "B", "b desc", "VARCHAR(5)", false, "NULL"),
            ]);

        var columns = await NetezzaSchemaLoader.HydrateColumnsAsync(connection, "SALES", "PUBLIC", "T1");

        Assert.Equal(2, columns.Count);
        Assert.Equal("A", columns[0].Name);
        Assert.Equal("VARCHAR(5)", columns[1].DataType);
        Assert.Equal("b desc", columns[1].Description);
        Assert.True(columns[1].Nullable);
    }
}
