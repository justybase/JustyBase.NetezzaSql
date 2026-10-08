using JustyBase.Netezza.Models;
using JustyBase.Netezza.Schema;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Netezza.Tests;

/// <summary>
/// Production key-metadata tests: raw catalog key rows become declared
/// <see cref="NetezzaReferenceKey"/> records and reach the parser schema provider
/// through the normal adapter path (never through the conformance harness).
/// </summary>
public sealed class NetezzaSchemaKeyLoadingTests
{
    private static NetezzaSchemaTable Table(string name, string schema, string database, int catalogId) =>
        new(name, schema, database, CatalogId: catalogId);

    [Fact]
    public void AttachReferenceKeys_BuildsCompositeForeignKeyInCatalogOrder()
    {
        var tables = new List<NetezzaSchemaTable>
        {
            Table("SHIPMENT", "SALES", "JB_REL", 10),
            Table("ORDER_LINE", "SALES", "JB_REL", 11),
        };
        var rows = new List<NetezzaSchemaKeyRow>
        {
            new(10, "SALES", "SHIPMENT", "FK_SHIPMENT_LINE", "f", "ORDER_ID", "JB_REL", "SALES", "ORDER_LINE", "ORDER_ID"),
            new(10, "SALES", "SHIPMENT", "FK_SHIPMENT_LINE", "f", "LINE_NO", "JB_REL", "SALES", "ORDER_LINE", "LINE_NO"),
        };

        var result = NetezzaSchemaLoader.AttachReferenceKeys(tables, rows, "JB_REL");

        var key = Assert.Single(result.Single(table => table.Name == "SHIPMENT").Keys!);
        Assert.True(key.IsForeignKey);
        Assert.Equal("FK_SHIPMENT_LINE", key.ConstraintName);
        Assert.Equal(["ORDER_ID", "LINE_NO"], key.Columns);
        Assert.Equal(["ORDER_ID", "LINE_NO"], key.ReferencedColumns);
        Assert.Equal("ORDER_LINE", key.ReferencedTable);
        Assert.Equal("SALES", key.ReferencedSchema);
        Assert.Equal("JB_REL", key.ReferencedDatabase);
        Assert.Null(result.Single(table => table.Name == "ORDER_LINE").Keys);
    }

    [Fact]
    public void AttachReferenceKeys_BuildsPrimaryKeyFromCatalogRows()
    {
        var tables = new List<NetezzaSchemaTable> { Table("ORDER_LINE", "SALES", "JB_REL", 11) };
        var rows = new List<NetezzaSchemaKeyRow>
        {
            new(11, "SALES", "ORDER_LINE", "PK_ORDER_LINE", "p", "ORDER_ID"),
            new(11, "SALES", "ORDER_LINE", "PK_ORDER_LINE", "p", "LINE_NO"),
        };

        var result = NetezzaSchemaLoader.AttachReferenceKeys(tables, rows, "JB_REL");

        var key = Assert.Single(result.Single(table => table.Name == "ORDER_LINE").Keys!);
        Assert.True(key.IsPrimaryKey);
        Assert.False(key.IsForeignKey);
        Assert.Equal(["ORDER_ID", "LINE_NO"], key.Columns);
        Assert.Equal("ORDER_LINE", key.ReferencedTable);
        Assert.Equal(["ORDER_ID", "LINE_NO"], key.ReferencedColumns);
    }

    [Fact]
    public void AttachReferenceKeys_PreservesCrossSchemaAndDatabaseEndpoint()
    {
        var tables = new List<NetezzaSchemaTable>
        {
            Table("ORDERS_2023", "ARCHIVE", "JB_REL", 20),
            Table("CUSTOMER", "SALES", "JB_REL", 21),
        };
        var rows = new List<NetezzaSchemaKeyRow>
        {
            new(20, "ARCHIVE", "ORDERS_2023", "FK_ARCH_CUSTOMER", "f", "CUSTOMER_ID", "JB_REL", "SALES", "CUSTOMER", "CUSTOMER_ID"),
        };

        var result = NetezzaSchemaLoader.AttachReferenceKeys(tables, rows, "JB_REL");

        var key = Assert.Single(result.Single(table => table.Name == "ORDERS_2023").Keys!);
        Assert.Equal("JB_REL", key.ReferencedDatabase);
        Assert.Equal("SALES", key.ReferencedSchema);
        Assert.Equal("CUSTOMER", key.ReferencedTable);
    }

    [Fact]
    public void AttachReferenceKeys_IgnoresOrphanAndEmptyRows()
    {
        var tables = new List<NetezzaSchemaTable> { Table("ORDERS", "SALES", "JB_REL", 30) };
        var rows = new List<NetezzaSchemaKeyRow>
        {
            new(999, "SALES", "UNKNOWN", "FK_X", "f", "CUSTOMER_ID"),
            new(30, "SALES", "ORDERS", "FK_EMPTY", "f", string.Empty),
        };

        var result = NetezzaSchemaLoader.AttachReferenceKeys(tables, rows, "JB_REL");

        Assert.Null(result.Single().Keys);
    }

    [Fact]
    public void Adapter_ProjectsDeclaredForeignKeys_AndNotPrimaryKeys()
    {
        var provider = new InMemorySchemaProvider();
        var snapshot = new NetezzaSchemaSnapshot(
        [
            new NetezzaSchemaTable("ORDER_LINE", "SALES", "JB_REL", CatalogId: 11,
                Keys:
                [
                    new NetezzaReferenceKey("PK_ORDER_LINE", "p", ["ORDER_ID", "LINE_NO"]),
                ]),
            new NetezzaSchemaTable("SHIPMENT", "SALES", "JB_REL", CatalogId: 10,
                Keys:
                [
                    new NetezzaReferenceKey(
                        "FK_SHIPMENT_LINE", "f", ["ORDER_ID", "LINE_NO"],
                        "JB_REL", "SALES", "ORDER_LINE", ["ORDER_ID", "LINE_NO"]),
                ]),
        ]);

        NetezzaSchemaProviderAdapter.Apply(provider, snapshot);

        Assert.Null(provider.GetForeignKeys("JB_REL", "SALES", "ORDER_LINE"));

        var relation = Assert.Single(provider.GetForeignKeys("JB_REL", "SALES", "SHIPMENT")!);
        Assert.Equal(["ORDER_ID", "LINE_NO"], relation.Columns);
        Assert.Equal(["ORDER_ID", "LINE_NO"], relation.ReferencedColumns);
        Assert.Equal("ORDER_LINE", relation.ReferencedTable);
        Assert.Equal("SALES", relation.ReferencedSchema);
        Assert.Equal("JB_REL", relation.ReferencedDatabase);
        Assert.Equal("FK_SHIPMENT_LINE", relation.Name);

        // Reverse direction is discovered by the completion layer from this one relation.
        Assert.Null(provider.GetForeignKeys("JB_REL", "SALES", "ORDER_LINE"));
    }

    [Fact]
    public void Adapter_SkipsMalformedReferencedColumnCount()
    {
        var provider = new InMemorySchemaProvider();
        var snapshot = new NetezzaSchemaSnapshot(
        [
            new NetezzaSchemaTable("SHIPMENT", "SALES", "JB_REL",
                Keys:
                [
                    new NetezzaReferenceKey("FK_BAD", "f", ["ORDER_ID", "LINE_NO"], "JB_REL", "SALES", "ORDER_LINE", ["ORDER_ID"]),
                ]),
        ]);

        NetezzaSchemaProviderAdapter.Apply(provider, snapshot);

        Assert.Null(provider.GetForeignKeys("JB_REL", "SALES", "SHIPMENT"));
    }

    [Fact]
    public void AdapterProjectedForeignKey_ReachesProductionJoinTargetCompletion()
    {
        var provider = new InMemorySchemaProvider();
        var snapshot = new NetezzaSchemaSnapshot(
        [
            new NetezzaSchemaTable("ORDER_LINE", "SALES", "JB_REL", Columns:
            [
                new NetezzaSchemaColumn("ORDER_ID", "INTEGER"),
                new NetezzaSchemaColumn("LINE_NO", "INTEGER"),
            ]),
            new NetezzaSchemaTable("SHIPMENT", "SALES", "JB_REL", Columns:
            [
                new NetezzaSchemaColumn("SHIPMENT_ID", "INTEGER"),
                new NetezzaSchemaColumn("ORDER_ID", "INTEGER"),
                new NetezzaSchemaColumn("LINE_NO", "INTEGER"),
            ],
                Keys:
                [
                    new NetezzaReferenceKey("FK_SHIPMENT_LINE", "f", ["ORDER_ID", "LINE_NO"], "JB_REL", "SALES", "ORDER_LINE", ["ORDER_ID", "LINE_NO"]),
                ]),
        ]);

        NetezzaSchemaProviderAdapter.Apply(provider, snapshot);

        const string sql = "SELECT * FROM SALES.SHIPMENT S JOIN ";
        var engine = new NzCompletionEngine(provider, dialect: SqlDialect.Netezza);
        var items = engine.GetCompletions(sql, sql.Length);

        var target = Assert.Single(items, item => item.Label == "ORDER_LINE" && item.Kind == CompletionKind.Table);
        Assert.Equal("JOIN with declared foreign key", target.Detail);
        Assert.Contains("S.ORDER_ID = OL.ORDER_ID AND S.LINE_NO = OL.LINE_NO", target.InsertText);
    }
}
