using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.NetezzaSql.Tests;

/// <summary>Column hover, catalog targets and rename built on column identity.</summary>
public sealed class ColumnAuthoringTests
{
    private static InMemorySchemaProvider Schema()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("CUSTOMERS", "SALES", "SHOP",
            Columns: [new ColumnInfo("CUSTOMER_ID", DataType: "INTEGER"), new ColumnInfo("CUSTOMER_NAME", DataType: "VARCHAR(120)"),
                new ColumnInfo("EMAIL", DataType: "VARCHAR(255)")]));
        schema.AddTable(new TableInfo("ORDERS", "SALES", "SHOP",
            Columns: [new ColumnInfo("ORDER_ID", DataType: "BIGINT"), new ColumnInfo("CUSTOMER_ID", DataType: "INTEGER"),
                new ColumnInfo("ORDER_DATE", DataType: "DATE")]));
        return schema;
    }

    private static (string Sql, int Cursor) At(string sqlWithCaret)
    {
        var cursor = sqlWithCaret.IndexOf('|');
        return (sqlWithCaret.Remove(cursor, 1), cursor);
    }

    private static string? Rename(string sqlWithCaret, string newName)
    {
        var (sql, cursor) = At(sqlWithCaret);
        var edits = NzColumnIdentityService.GetRenameEdits(sql, cursor, newName, Schema());
        if (edits is null) return null;
        foreach (var edit in edits.Reverse()) sql = sql[..edit.StartOffset] + edit.NewText + sql[edit.EndOffset..];
        return sql;
    }

    [Fact]
    public void Hover_CteColumn_ShowsRelationOriginAndType()
    {
        var (sql, cursor) = At("WITH X AS (SELECT CUSTOMER_ID AS CID FROM SHOP.SALES.CUSTOMERS) SELECT X.|CID FROM X");
        var hover = NzHoverService.GetHover(sql, cursor, Schema());
        Assert.NotNull(hover);
        Assert.Equal("Column", hover!.TargetKind);
        Assert.Equal(new SqlCatalogColumn("SHOP", "SALES", "CUSTOMERS", "CUSTOMER_ID"), hover.Column!.Origin);
        Assert.Equal("INTEGER", hover.Column.DataType);
        Assert.Contains("CTE: `X`", hover.Content);
        Assert.Contains("origin: `SHOP.SALES.CUSTOMERS.CUSTOMER_ID`", hover.Content);
        Assert.Contains("type: `INTEGER`", hover.Content);
    }

    [Fact]
    public void Hover_ComputedColumn_NeverFabricatesOriginOrType()
    {
        var (sql, cursor) = At("WITH X AS (SELECT CUSTOMER_ID + 1 AS NEXT_ID FROM SHOP.SALES.CUSTOMERS) SELECT X.|NEXT_ID FROM X");
        var hover = NzHoverService.GetHover(sql, cursor, Schema());
        Assert.Null(hover!.Column!.Origin);
        Assert.Null(hover.Column.DataType);
        Assert.DoesNotContain("origin:", hover.Content);
        Assert.DoesNotContain("type:", hover.Content);
    }

    [Fact]
    public void Hover_AmbiguousColumn_ListsCandidates()
    {
        var (sql, cursor) = At("SELECT |CUSTOMER_ID FROM SHOP.SALES.CUSTOMERS C JOIN SHOP.SALES.ORDERS O ON O.ORDER_ID = 1");
        var hover = NzHoverService.GetHover(sql, cursor, Schema());
        Assert.Equal(SqlColumnResolutionStatus.Ambiguous, hover!.Column!.Status);
        Assert.Null(hover.Column.Origin);
        Assert.Contains("ambiguous: `CUSTOMERS`, `ORDERS`", hover.Content);
    }

    [Fact]
    public void Hover_AsksOnlyForReferencedRelations()
    {
        var counting = new CountingSchema(Schema());
        var (sql, cursor) = At("SELECT C.|EMAIL FROM SHOP.SALES.CUSTOMERS C");
        Assert.Equal("VARCHAR(255)", NzHoverService.GetHover(sql, cursor, counting)!.Column!.DataType);
        Assert.Equal(["CUSTOMERS"], counting.TablesRequested);
    }

    [Fact]
    public void CatalogTarget_PhysicalColumnAndLocalOrigin()
    {
        var (physical, physicalCursor) = At("SELECT C.|EMAIL FROM SHOP.SALES.CUSTOMERS C");
        Assert.Equal(new SqlColumnCatalogTarget("SHOP", "SALES", "CUSTOMERS", "EMAIL", SqlColumnTargetVia.Catalog),
            NzColumnIdentityService.GetCatalogTarget(physical, physicalCursor, Schema()));
        var (local, localCursor) = At("SELECT D.|K FROM (SELECT ORDER_ID AS K FROM SHOP.SALES.ORDERS) D");
        Assert.Equal(new SqlColumnCatalogTarget("SHOP", "SALES", "ORDERS", "ORDER_ID", SqlColumnTargetVia.Origin),
            NzColumnIdentityService.GetCatalogTarget(local, localCursor, Schema()));
        var (computed, computedCursor) = At("WITH X AS (SELECT 1 AS ONE) SELECT X.|ONE FROM X");
        Assert.Null(NzColumnIdentityService.GetCatalogTarget(computed, computedCursor, Schema()));
    }

    [Fact]
    public void Rename_CteAlias_EditsOnlyItsIdentity()
    {
        Assert.Equal(
            "WITH X AS (SELECT CUSTOMER_ID AS KEY_ID FROM SHOP.SALES.CUSTOMERS) SELECT X.KEY_ID, CUSTOMER_ID FROM X JOIN SHOP.SALES.CUSTOMERS C ON C.CUSTOMER_ID = X.KEY_ID",
            Rename("WITH X AS (SELECT CUSTOMER_ID AS CID FROM SHOP.SALES.CUSTOMERS) SELECT X.|CID, CUSTOMER_ID FROM X JOIN SHOP.SALES.CUSTOMERS C ON C.CUSTOMER_ID = X.CID", "KEY_ID"));
    }

    [Fact]
    public void Rename_RejectsPhysicalAmbiguousPassThroughAndStarColumns()
    {
        Assert.Null(Rename("SELECT C.|EMAIL FROM SHOP.SALES.CUSTOMERS C", "MAIL"));
        Assert.Null(Rename("SELECT |CUSTOMER_ID FROM SHOP.SALES.CUSTOMERS C JOIN SHOP.SALES.ORDERS O ON O.ORDER_ID = 1", "K"));
        Assert.Null(Rename("WITH X AS (SELECT CUSTOMER_ID FROM SHOP.SALES.CUSTOMERS) SELECT X.|CUSTOMER_ID FROM X", "K"));
        Assert.Null(Rename("WITH X AS (SELECT * FROM SHOP.SALES.CUSTOMERS) SELECT X.|EMAIL FROM X", "MAIL"));
        Assert.Null(Rename("SELECT E.CID FROM (SELECT D.CID FROM (SELECT CUSTOMER_ID AS |CID FROM SHOP.SALES.CUSTOMERS) D) E", "K"));
    }

    [Fact]
    public void Rename_RejectsCaptureAmbiguityAndSiblingCollision()
    {
        const string sql = "WITH X AS (SELECT CUSTOMER_ID AS CID FROM SHOP.SALES.CUSTOMERS) SELECT X.|CID, ORDER_DATE FROM X JOIN SHOP.SALES.ORDERS O ON O.CUSTOMER_ID = X.CID";
        Assert.Null(Rename(sql, "ORDER_DATE"));
        Assert.NotNull(Rename(sql, "ORDER_ID"));
        Assert.Null(Rename("WITH X AS (SELECT CUSTOMER_ID AS CID FROM SHOP.SALES.CUSTOMERS) SELECT |CID FROM X JOIN SHOP.SALES.ORDERS O ON O.CUSTOMER_ID = X.CID", "ORDER_ID"));
        Assert.Null(Rename("WITH X AS (SELECT CUSTOMER_ID AS |CID, EMAIL AS NM FROM SHOP.SALES.CUSTOMERS) SELECT X.CID, X.NM FROM X", "NM"));
        Assert.Null(Rename("SELECT C.EMAIL FROM SHOP.SALES.CUSTOMERS C WHERE EXISTS (SELECT 1 FROM (SELECT ORDER_ID AS |OID FROM SHOP.SALES.ORDERS) D WHERE EMAIL IS NOT NULL)", "EMAIL"));
    }

    [Fact]
    public void Rename_QuotesReservedAndMalformedNamesAreRejected()
    {
        Assert.Equal("WITH X AS (SELECT CUSTOMER_ID AS \"ORDER\" FROM SHOP.SALES.CUSTOMERS) SELECT X.\"ORDER\" FROM X",
            Rename("WITH X AS (SELECT CUSTOMER_ID AS CID FROM SHOP.SALES.CUSTOMERS) SELECT X.|CID FROM X", "ORDER"));
        Assert.Null(Rename("WITH X AS (SELECT CUSTOMER_ID AS CID FROM SHOP.SALES.CUSTOMERS) SELECT X.|CID FROM X", "\"unterminated"));
        Assert.Equal("\"GROUP\"", NzRenameService.FormatReplacement("A", "GROUP"));
        Assert.Equal("ORDERS", NzRenameService.FormatReplacement("A", "ORDERS"));
    }

    [Fact]
    public void AliasedSelectItem_IsOneCteColumnForOuterReferenceAndOwnOrderBy()
    {
        const string sql = "WITH X AS (SELECT CUSTOMER_NAME AS NM FROM SHOP.SALES.CUSTOMERS ORDER BY NM) SELECT X.NM FROM X";
        var outer = NzColumnIdentityService.Resolve(sql, sql.LastIndexOf("NM", StringComparison.Ordinal), Schema())!;
        var inner = NzColumnIdentityService.Resolve(sql, sql.IndexOf("BY NM", StringComparison.Ordinal) + 3, Schema())!;
        Assert.Equal(SqlColumnRelationKind.Cte, outer.RelationKind);
        Assert.Equal(3, outer.Occurrences.Count);
        Assert.Equal(outer.Occurrences, inner.Occurrences);
        Assert.Equal("WITH X AS (SELECT CUSTOMER_NAME AS LABEL FROM SHOP.SALES.CUSTOMERS ORDER BY LABEL) SELECT X.LABEL FROM X",
            Rename("WITH X AS (SELECT CUSTOMER_NAME AS NM FROM SHOP.SALES.CUSTOMERS ORDER BY NM) SELECT X.|NM FROM X", "LABEL"));
        const string top = "SELECT CUSTOMER_NAME AS NM FROM SHOP.SALES.CUSTOMERS ORDER BY NM";
        Assert.Equal(SqlColumnRelationKind.OutputAlias,
            NzColumnIdentityService.Resolve(top, top.LastIndexOf("NM", StringComparison.Ordinal), Schema())!.RelationKind);
    }

    [Fact]
    public void Analysis_AnswersEveryOffsetLikeFreshResolution()
    {
        const string sql = "WITH X AS (SELECT CUSTOMER_ID AS CID FROM SHOP.SALES.CUSTOMERS) SELECT X.CID, X.CID + 1 FROM X WHERE X.CID > 0";
        var schema = Schema();
        var analysis = NzColumnIdentityService.Analyze(sql, schema)!;
        for (var offset = 0; offset < sql.Length; offset++)
        {
            var reused = analysis.Resolve(offset);
            var fresh = NzColumnIdentityService.Resolve(sql, offset, schema);
            Assert.Equal(fresh is null, reused is null);
            if (fresh is null) continue;
            Assert.Equal(fresh.Name, reused!.Name);
            Assert.Equal(fresh.Definition, reused.Definition);
            Assert.Equal(fresh.Occurrences, reused.Occurrences);
            Assert.Equal(fresh.DataType, reused.DataType);
        }
    }

    [Fact]
    public void Analysis_IsBoundToItsText()
    {
        var schema = Schema();
        var before = NzColumnIdentityService.Analyze("WITH X AS (SELECT CUSTOMER_ID AS CID FROM SHOP.SALES.CUSTOMERS) SELECT X.CID FROM X", schema)!;
        const string edited = "WITH X AS (SELECT CUSTOMER_ID AS KEY_ID FROM SHOP.SALES.CUSTOMERS) SELECT X.KEY_ID FROM X";
        var after = NzColumnIdentityService.Analyze(edited, schema)!;
        Assert.Equal("CID", before.Resolve(before.Text.LastIndexOf("CID", StringComparison.Ordinal))!.Name);
        Assert.Equal("KEY_ID", after.Resolve(edited.LastIndexOf("KEY_ID", StringComparison.Ordinal))!.Name);
        Assert.NotEqual(before.Text, after.Text);
    }

    private sealed class CountingSchema(ISchemaProvider inner) : ISchemaProvider
    {
        public List<string> TablesRequested { get; } = new();
        public bool TableExists(string? database, string? schema, string tableName) => inner.TableExists(database, schema, tableName);
        public bool HasTables() => inner.HasTables();
        public TableInfo? GetTable(string? database, string? schema, string tableName)
        {
            TablesRequested.Add(tableName);
            return inner.GetTable(database, schema, tableName);
        }
        public IReadOnlyList<(string Name, TableKind Kind)>? GetTableNames(string? database, string? schema) => throw new InvalidOperationException("catalog enumeration");
        public IReadOnlyList<string>? GetDatabases() => throw new InvalidOperationException("catalog enumeration");
        public IReadOnlyList<string>? GetSchemas(string? database) => throw new InvalidOperationException("catalog enumeration");
        public bool CanValidateUnqualifiedTableReferences() => inner.CanValidateUnqualifiedTableReferences();
        public void BumpMetadataEpoch() => inner.BumpMetadataEpoch();
        public int MetadataEpoch => inner.MetadataEpoch;
    }
}
