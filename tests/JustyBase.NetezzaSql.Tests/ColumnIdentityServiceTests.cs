using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.NetezzaSql.Tests;

public sealed class ColumnIdentityServiceTests
{
    private static InMemorySchemaProvider Schema()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("CUSTOMERS", "SALES", "SHOP",
            Columns: [new ColumnInfo("CUSTOMER_ID"), new ColumnInfo("CUSTOMER_NAME")]));
        schema.AddTable(new TableInfo("ORDERS", "SALES", "SHOP",
            Columns: [new ColumnInfo("ORDER_ID"), new ColumnInfo("CUSTOMER_ID")]));
        return schema;
    }

    private static (string Sql, int Cursor) At(string sqlWithCaret)
    {
        var cursor = sqlWithCaret.IndexOf('|');
        return (sqlWithCaret.Remove(cursor, 1), cursor);
    }

    private static SqlColumnIdentity Resolve(string sqlWithCaret, ISchemaProvider? schema = null)
    {
        var (sql, cursor) = At(sqlWithCaret);
        var identity = NzColumnIdentityService.Resolve(sql, cursor, schema ?? Schema());
        Assert.NotNull(identity);
        return identity!;
    }

    [Fact]
    public void QualifiedPhysicalColumn_ResolvesToCatalogTarget()
    {
        var identity = Resolve("SELECT C.|CUSTOMER_ID FROM SHOP.SALES.CUSTOMERS C");
        Assert.Equal(SqlColumnResolutionStatus.Resolved, identity.Status);
        Assert.Null(identity.Definition);
        Assert.Equal(new SqlCatalogColumn("SHOP", "SALES", "CUSTOMERS", "CUSTOMER_ID"), identity.Catalog);
    }

    [Fact]
    public void UnqualifiedColumnInTwoSources_IsAmbiguousNotGuessed()
    {
        var identity = Resolve("SELECT |CUSTOMER_ID FROM SHOP.SALES.CUSTOMERS C JOIN SHOP.SALES.ORDERS O ON O.ORDER_ID = 1");
        Assert.Equal(SqlColumnResolutionStatus.Ambiguous, identity.Status);
        Assert.Equal(["CUSTOMERS", "ORDERS"], identity.Candidates.OrderBy(name => name));
        Assert.Null(identity.Catalog);
        var (sql, cursor) = At("SELECT |CUSTOMER_ID FROM SHOP.SALES.CUSTOMERS C JOIN SHOP.SALES.ORDERS O ON O.ORDER_ID = 1");
        Assert.Empty(NzColumnIdentityService.GetReferences(sql, cursor, includeDeclaration: true, Schema()));
    }

    [Fact]
    public void CteOutputColumn_NavigatesToProjectionAndKeepsPhysicalOrigin()
    {
        var identity = Resolve("WITH X AS (SELECT CUSTOMER_ID AS CID FROM SHOP.SALES.CUSTOMERS) SELECT X.|CID FROM X");
        Assert.Equal(SqlColumnRelationKind.Cte, identity.RelationKind);
        Assert.Equal("X", identity.Relation);
        Assert.NotNull(identity.Definition);
        Assert.Equal(new SqlCatalogColumn("SHOP", "SALES", "CUSTOMERS", "CUSTOMER_ID"), identity.Origin);
    }

    [Fact]
    public void ExplicitCteColumnList_DefinesTheColumn()
    {
        var (sql, cursor) = At("WITH X (CID) AS (SELECT CUSTOMER_ID FROM SHOP.SALES.CUSTOMERS) SELECT |CID FROM X");
        var definition = NzColumnIdentityService.GetDefinition(sql, cursor, Schema());
        Assert.NotNull(definition);
        Assert.Equal(sql.IndexOf("CID", StringComparison.Ordinal), definition!.StartOffset);
    }

    [Fact]
    public void NestedDerivedProjection_ChainsOriginThroughEachLevel()
    {
        var identity = Resolve("SELECT E.|CID FROM (SELECT D.CID FROM (SELECT CUSTOMER_ID AS CID FROM SHOP.SALES.CUSTOMERS) D) E");
        Assert.Equal(SqlColumnRelationKind.DerivedTable, identity.RelationKind);
        Assert.Equal("E", identity.Relation);
        Assert.Equal("CUSTOMER_ID", identity.Origin?.Column);
    }

    [Fact]
    public void OrderByOutputAlias_ResolvesToTheAlias()
    {
        var identity = Resolve("SELECT CUSTOMER_NAME AS NM FROM SHOP.SALES.CUSTOMERS ORDER BY |NM");
        Assert.Equal(SqlColumnRelationKind.OutputAlias, identity.RelationKind);
        Assert.Equal("CUSTOMER_NAME", identity.Origin?.Column);
    }

    [Fact]
    public void CorrelatedReference_ResolvesInParentScope()
    {
        var identity = Resolve("SELECT 1 FROM SHOP.SALES.CUSTOMERS C WHERE EXISTS (SELECT 1 FROM SHOP.SALES.ORDERS O WHERE O.ORDER_ID = C.|CUSTOMER_ID)");
        Assert.Equal("CUSTOMERS", identity.Catalog?.Relation);
    }

    [Fact]
    public void ReferencesExcludeSameNamedColumnFromAnotherSource()
    {
        var (sql, cursor) = At("SELECT C.CUSTOMER_ID, O.|CUSTOMER_ID FROM SHOP.SALES.CUSTOMERS C JOIN SHOP.SALES.ORDERS O ON O.CUSTOMER_ID = C.CUSTOMER_ID");
        var references = NzColumnIdentityService.GetReferences(sql, cursor, includeDeclaration: false, Schema());
        Assert.Equal(2, references.Count);
        Assert.All(references, reference => Assert.Equal("O.", sql[(reference.StartOffset - 2)..reference.StartOffset]));
    }

    [Fact]
    public void WithoutMetadata_SingleSourceResolvesUnverified_TwoSourcesStayUnresolved()
    {
        var empty = new InMemorySchemaProvider();
        var single = Resolve("SELECT |ID FROM T", empty);
        Assert.Equal(SqlColumnResolutionStatus.Resolved, single.Status);
        Assert.Equal(new SqlCatalogColumn(null, null, "T", "ID"), single.Catalog);

        var two = Resolve("SELECT |ID FROM T JOIN U ON 1 = 1", empty);
        Assert.Equal(SqlColumnResolutionStatus.Unresolved, two.Status);
    }

    [Fact]
    public void ScriptLocalCtasColumn_ResolvesToProjection()
    {
        var identity = Resolve("CREATE TEMP TABLE T1 AS SELECT CUSTOMER_ID AS CID FROM SHOP.SALES.CUSTOMERS;\nSELECT T1.|CID FROM T1");
        Assert.Equal(SqlColumnRelationKind.ScriptLocalTable, identity.RelationKind);
        Assert.Equal("T1", identity.Relation);
        Assert.Equal("CUSTOMER_ID", identity.Origin?.Column);
    }

    [Fact]
    public void IncompleteSql_DoesNotThrow()
    {
        var (sql, cursor) = At("SELECT C.|CUSTOMER_ID FROM SHOP.SALES.CUSTOMERS C WHERE");
        var exception = Record.Exception(() => NzColumnIdentityService.Resolve(sql, cursor, Schema()));
        Assert.Null(exception);
    }

    [Fact]
    public void Resolve_OnlyAsksForReferencedRelations_AndNeverEnumeratesTheCatalog()
    {
        var counting = new CountingSchemaProvider(Schema());
        var sql = "SELECT C.CUSTOMER_ID FROM SHOP.SALES.CUSTOMERS C WHERE "
            + string.Join(" OR ", Enumerable.Repeat("C.CUSTOMER_ID = 1", 500));
        var identity = NzColumnIdentityService.Resolve(sql, sql.IndexOf("CUSTOMER_ID", StringComparison.Ordinal), counting);

        Assert.Equal(501, identity!.Occurrences.Count);
        // One FROM source: one lookup, independent of the 501 references.
        Assert.Equal(["CUSTOMERS"], counting.TablesRequested);
        Assert.Equal(0, counting.CatalogEnumerations);
    }

    private sealed class CountingSchemaProvider(ISchemaProvider inner) : ISchemaProvider
    {
        public List<string> TablesRequested { get; } = new();
        public int CatalogEnumerations { get; private set; }
        public bool TableExists(string? database, string? schema, string tableName) => inner.TableExists(database, schema, tableName);
        public bool HasTables() => inner.HasTables();
        public TableInfo? GetTable(string? database, string? schema, string tableName)
        {
            TablesRequested.Add(tableName);
            return inner.GetTable(database, schema, tableName);
        }
        public IReadOnlyList<(string Name, TableKind Kind)>? GetTableNames(string? database, string? schema)
        {
            CatalogEnumerations++;
            return inner.GetTableNames(database, schema);
        }
        public IReadOnlyList<string>? GetDatabases()
        {
            CatalogEnumerations++;
            return inner.GetDatabases();
        }
        public IReadOnlyList<string>? GetSchemas(string? database)
        {
            CatalogEnumerations++;
            return inner.GetSchemas(database);
        }
        public bool CanValidateUnqualifiedTableReferences() => inner.CanValidateUnqualifiedTableReferences();
        public void BumpMetadataEpoch() => inner.BumpMetadataEpoch();
        public int MetadataEpoch => inner.MetadataEpoch;
    }
}
