using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.NetezzaSql.Tests;

public sealed class JoinRelationCompletionTests
{
    private static InMemorySchemaProvider CreateSchema()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("ORDERS", "PUBLIC", "TESTDB", Columns:
        [
            new ColumnInfo("ORDER_ID", DataType: "INT4"),
            new ColumnInfo("CUSTOMER_ID", DataType: "INT4")
        ]));
        schema.AddTable(new TableInfo("CUSTOMERS", "PUBLIC", "TESTDB", Columns:
        [
            new ColumnInfo("ID", DataType: "INT4"),
            new ColumnInfo("NAME", DataType: "VARCHAR(50)")
        ]));
        return schema;
    }

    [Fact]
    public void InMemorySchemaProvider_ExposesRegisteredForeignKeys()
    {
        var schema = CreateSchema();
        var relation = new ForeignKeyRelation(["CUSTOMER_ID"], "CUSTOMERS", ["ID"], ReferencedSchema: "PUBLIC");
        schema.AddForeignKey("TESTDB", "PUBLIC", "ORDERS", relation);

        IForeignKeyProvider provider = schema;
        var relations = provider.GetForeignKeys("TESTDB", "PUBLIC", "ORDERS");

        Assert.NotNull(relations);
        var stored = Assert.Single(relations!);
        Assert.Equal("CUSTOMERS", stored.ReferencedTable);
        Assert.Equal(["CUSTOMER_ID"], stored.Columns);
        Assert.Equal(["ID"], stored.ReferencedColumns);
    }

    [Fact]
    public void InMemorySchemaProvider_UnqualifiedLookupFindsForeignKey()
    {
        var schema = CreateSchema();
        schema.AddForeignKey("TESTDB", "PUBLIC", "ORDERS",
            new ForeignKeyRelation(["CUSTOMER_ID"], "CUSTOMERS", ["ID"]));

        IForeignKeyProvider provider = schema;
        Assert.NotNull(provider.GetForeignKeys(null, null, "ORDERS"));
        Assert.Null(provider.GetForeignKeys(null, null, "UNKNOWN"));
    }

    [Fact]
    public void InMemorySchemaProvider_ReplaceForeignKeys_ClearsRemovedRelations()
    {
        var schema = CreateSchema();
        schema.AddForeignKey("TESTDB", "PUBLIC", "ORDERS",
            new ForeignKeyRelation(["CUSTOMER_ID"], "CUSTOMERS", ["ID"]));

        schema.ReplaceForeignKeys("TESTDB", "PUBLIC", "ORDERS", Array.Empty<ForeignKeyRelation>());

        Assert.Null(schema.GetForeignKeys("TESTDB", "PUBLIC", "ORDERS"));
    }

    [Fact]
    public void Engine_EmptyOnClause_SuggestsForeignKeyPredicate()
    {
        var schema = CreateSchema();
        schema.AddForeignKey("TESTDB", "PUBLIC", "ORDERS",
            new ForeignKeyRelation(["CUSTOMER_ID"], "CUSTOMERS", ["ID"], ReferencedSchema: "PUBLIC"));

        const string sql = "SELECT * FROM ORDERS O JOIN CUSTOMERS C ON ";
        var engine = new NzCompletionEngine(schema, dialect: SqlDialect.Netezza);

        var items = engine.GetCompletions(sql, sql.Length);

        var predicate = Assert.Single(items, item => item.Detail == "foreign key");
        Assert.Equal("O.CUSTOMER_ID = C.ID", predicate.Label);
    }

    [Fact]
    public void Engine_EmptyOnClause_UsesReverseForeignKeyDirection()
    {
        var schema = CreateSchema();
        schema.AddForeignKey("TESTDB", "PUBLIC", "CUSTOMERS",
            new ForeignKeyRelation(["SALES_REP_ID"], "ORDERS", ["ORDER_ID"], ReferencedSchema: "PUBLIC"));

        const string sql = "SELECT * FROM ORDERS O JOIN CUSTOMERS C ON ";
        var engine = new NzCompletionEngine(schema, dialect: SqlDialect.Netezza);

        var items = engine.GetCompletions(sql, sql.Length);

        var predicate = Assert.Single(items, item => item.Detail == "foreign key");
        Assert.Equal("O.ORDER_ID = C.SALES_REP_ID", predicate.Label);
    }

    [Fact]
    public void Engine_NestedQuerySuggestsRelationForInnermostJoinOnly()
    {
        var schema = CreateSchema();
        schema.AddForeignKey("TESTDB", "PUBLIC", "ORDERS",
            new ForeignKeyRelation(["CUSTOMER_ID"], "CUSTOMERS", ["ID"], ReferencedSchema: "PUBLIC"));

        const string sql = "SELECT * FROM ORDERS O JOIN CUSTOMERS C ON EXISTS (SELECT * FROM ORDERS O2 JOIN CUSTOMERS C2 ON ";
        var engine = new NzCompletionEngine(schema, dialect: SqlDialect.Netezza);

        var items = engine.GetCompletions(sql, sql.Length);

        Assert.Contains(items, item => item.Label == "O2.CUSTOMER_ID = C2.ID");
        Assert.DoesNotContain(items, item => item.Label == "O.CUSTOMER_ID = C.ID");
    }

    [Fact]
    public void Engine_ReferencedDatabaseMismatch_DoesNotSuggestRelation()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("ORDERS", "PUBLIC", "DB1", Columns: [new ColumnInfo("CUSTOMER_ID")]));
        schema.AddTable(new TableInfo("CUSTOMERS", "PUBLIC", "DB1", Columns: [new ColumnInfo("ID")]));
        schema.AddForeignKey("DB1", "PUBLIC", "ORDERS",
            new ForeignKeyRelation(["CUSTOMER_ID"], "CUSTOMERS", ["ID"], ReferencedSchema: "PUBLIC", ReferencedDatabase: "DB2"));

        const string sql = "SELECT * FROM DB1..ORDERS O JOIN DB1..CUSTOMERS C ON ";
        var engine = new NzCompletionEngine(schema, dialect: SqlDialect.Netezza);

        var items = engine.GetCompletions(sql, sql.Length);

        Assert.DoesNotContain(items, item => item.Detail == "foreign key");
    }

    [Fact]
    public void Engine_AutomaticTrigger_StillOffersDeclaredJoinTargets()
    {
        var schema = CreateSchema();
        schema.AddForeignKey("TESTDB", "PUBLIC", "ORDERS",
            new ForeignKeyRelation(["CUSTOMER_ID"], "CUSTOMERS", ["ID"], ReferencedSchema: "PUBLIC"));

        const string sql = "SELECT * FROM ORDERS O JOIN ";
        var engine = new NzCompletionEngine(schema, dialect: SqlDialect.Netezza)
        {
            TriggerKind = CompletionTriggerKind.Automatic,
        };

        var items = engine.GetCompletions(sql, sql.Length);

        Assert.Contains(items, item => item.Detail == "JOIN with declared foreign key");
    }

    [Fact]
    public void Engine_NoForeignKeyProvider_SuggestsNoPredicates()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("ORDERS", "PUBLIC", "TESTDB", Columns: [new ColumnInfo("ORDER_ID")]));
        schema.AddTable(new TableInfo("CUSTOMERS", "PUBLIC", "TESTDB", Columns: [new ColumnInfo("ID")]));

        const string sql = "SELECT * FROM ORDERS O JOIN CUSTOMERS C ON ";
        var engine = new NzCompletionEngine(schema, dialect: SqlDialect.Netezza);

        var items = engine.GetCompletions(sql, sql.Length);

        Assert.DoesNotContain(items, item => item.Detail == "foreign key");
    }
}
