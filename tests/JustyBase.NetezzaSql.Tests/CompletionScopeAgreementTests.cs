using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.NetezzaSql.Tests;

/// <summary>Qualified column completion agrees with column identity for `*` expansions.</summary>
public sealed class CompletionScopeAgreementTests
{
    private static NzCompletionEngine Engine()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("CUSTOMERS", "SALES", "SHOP",
            Columns: [new ColumnInfo("CUSTOMER_ID"), new ColumnInfo("EMAIL")]));
        schema.AddTable(new TableInfo("ORDERS", "SALES", "SHOP",
            Columns: [new ColumnInfo("ORDER_ID"), new ColumnInfo("ORDER_DATE")]));
        return new NzCompletionEngine(schema);
    }

    private static string[] Labels(string sqlWithCaret)
    {
        var cursor = sqlWithCaret.IndexOf('|');
        return Engine().GetCompletions(sqlWithCaret.Remove(cursor, 1), cursor).Select(item => item.Label).ToArray();
    }

    private static string[] Labels(ISchemaProvider schema, string sqlWithCaret)
    {
        var cursor = sqlWithCaret.IndexOf('|');
        return new NzCompletionEngine(schema)
            .GetCompletions(sqlWithCaret.Remove(cursor, 1), cursor)
            .Select(item => item.Label)
            .ToArray();
    }

    /// <summary>
    /// Rejects unqualified metadata lookups, mirroring hosts whose catalog
    /// lookups require an explicit database or database.schema path.
    /// </summary>
    private sealed class ExplicitQualifierSchemaProvider : ISchemaProvider
    {
        private readonly InMemorySchemaProvider _inner = new();

        public void AddTable(TableInfo table) => _inner.AddTable(table);

        public bool TableExists(string? database, string? schema, string tableName)
            => GetTable(database, schema, tableName) is not null;

        public bool HasTables() => _inner.HasTables();

        public TableInfo? GetTable(string? database, string? schema, string tableName)
            => database is null && schema is null ? null : _inner.GetTable(database, schema, tableName);

        public IReadOnlyList<(string Name, TableKind Kind)>? GetTableNames(string? database, string? schema)
            => database is null && schema is null ? Array.Empty<(string, TableKind)>() : _inner.GetTableNames(database, schema);

        public IReadOnlyList<string>? GetDatabases() => _inner.GetDatabases();

        public IReadOnlyList<string>? GetSchemas(string? database) => _inner.GetSchemas(database);

        public bool CanValidateUnqualifiedTableReferences() => false;

        public void BumpMetadataEpoch() => _inner.BumpMetadataEpoch();

        public int MetadataEpoch => _inner.MetadataEpoch;
    }

    [Theory]
    [InlineData("SELECT D.| FROM (SELECT C.*, 1 AS EXTRA FROM SHOP.SALES.CUSTOMERS C) D")]
    [InlineData("WITH X AS (SELECT C.*, 1 AS EXTRA FROM SHOP.SALES.CUSTOMERS C) SELECT X.| FROM X")]
    public void QualifiedStar_ExpandsSourceColumns_AndIsNeverAColumn(string sql)
    {
        var labels = Labels(sql);
        Assert.Contains("EMAIL", labels, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("EXTRA", labels, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("*", labels);
    }

    [Fact]
    public void CreateTableAsSelectStar_ExposesSourceColumns()
    {
        var labels = Labels("CREATE TEMP TABLE T1 AS SELECT * FROM SHOP.SALES.ORDERS;\nSELECT T1.| FROM T1");
        Assert.Contains("ORDER_DATE", labels, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("EMAIL", labels, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void AliasedCteOverDoubleDotStar_ResolvesColumnsAfterWhere()
    {
        var schema = new ExplicitQualifierSchemaProvider();
        schema.AddTable(new TableInfo("DIMDATE", "PUBLIC", "JUST_DATA",
            Columns: [new ColumnInfo("DATEKEY"), new ColumnInfo("CALENDARQUARTER")]));

        var labels = Labels(schema,
            "WITH XXX AS (SELECT * FROM JUST_DATA..DIMDATE) SELECT * FROM XXX AS Y WHERE Y.|");

        Assert.Contains("DATEKEY", labels, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("CALENDARQUARTER", labels, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void AliasedCteOverJoinStar_ResolvesColumnsFromEverySource()
    {
        var schema = new ExplicitQualifierSchemaProvider();
        schema.AddTable(new TableInfo("CUSTOMERS", "SALES", "JUST_DATA",
            Columns: [new ColumnInfo("CUSTOMER_ID"), new ColumnInfo("EMAIL")]));
        schema.AddTable(new TableInfo("ORDERS", "SALES", "JUST_DATA",
            Columns: [new ColumnInfo("ORDER_ID"), new ColumnInfo("CUSTOMER_ID")]));

        var labels = Labels(schema,
            "WITH X AS (SELECT * FROM JUST_DATA.SALES.CUSTOMERS C JOIN JUST_DATA.SALES.ORDERS O ON C.CUSTOMER_ID = O.CUSTOMER_ID) SELECT * FROM X AS Y WHERE Y.|");

        Assert.Contains("EMAIL", labels, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ORDER_ID", labels, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void DerivedStarPlusExplicitInJoinOn_ResolvesAllColumns()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("FACTFINANCE", "PUBLIC", "JUST_DATA",
            Columns: [new ColumnInfo("ACCOUNTKEY"), new ColumnInfo("ACCOUNTNAME")]));
        schema.AddTable(new TableInfo("DIMDATE", "PUBLIC", "JUST_DATA",
            Columns: [new ColumnInfo("DATEKEY"), new ColumnInfo("CALENDARQUARTER")]));

        const string sql = "WITH XXX AS (SELECT 2 AS COL1, * FROM JUST_DATA..DIMDATE) SELECT * FROM XXX Y JOIN (SELECT *, 2 AS TWO FROM FACTFINANCE) Z ON Y.CALENDARQUARTER = Z.";

        foreach (var activeDatabase in new[] { null, "JUST_DATA" })
        {
            var labels = new NzCompletionEngine(schema, activeDatabase: activeDatabase)
                .GetCompletions(sql, sql.Length)
                .Select(item => item.Label)
                .ToArray();

            Assert.True(labels.Contains("TWO", StringComparer.OrdinalIgnoreCase),
                $"activeDatabase={activeDatabase ?? "<null>"} labels: {string.Join(", ", labels)}");
            Assert.True(labels.Contains("ACCOUNTKEY", StringComparer.OrdinalIgnoreCase),
                $"activeDatabase={activeDatabase ?? "<null>"} labels: {string.Join(", ", labels)}");
            Assert.True(labels.Contains("ACCOUNTNAME", StringComparer.OrdinalIgnoreCase),
                $"activeDatabase={activeDatabase ?? "<null>"} labels: {string.Join(", ", labels)}");
        }
    }

    [Fact]
    public async Task OrchestratorHydration_CompletesDerivedStarColumnsFromLazyMetadata()
    {
        var schema = new InMemorySchemaProvider();
        // Lazy metadata: the relation name is known but its columns are not.
        schema.AddTable(new TableInfo("FACTFINANCE", "PUBLIC", "JUST_DATA"));
        schema.AddTable(new TableInfo("DIMDATE", "PUBLIC", "JUST_DATA",
            Columns: [new ColumnInfo("DATEKEY"), new ColumnInfo("CALENDARQUARTER")]));

        const string sql = "WITH XXX AS (SELECT 2 AS COL1, * FROM JUST_DATA..DIMDATE) "
                           + "SELECT * FROM XXX Y JOIN (SELECT *, 2 AS TWO FROM FACTFINANCE) Z "
                           + "ON Y.CALENDARQUARTER = Z.";

        var result = await CompletionOrchestrator.GetCompletions(
            sql,
            sql.Length,
            schema,
            SqlDialect.Netezza,
            options: new CompletionOrchestrationOptions
            {
                ForcedAutocomplete = true,
                HydrateColumns = (text, position, items) =>
                {
                    var tokens = NzLexer.Tokenize(text).ToArray();
                    var path = CompletionAliasResolver.ResolveTablePath(tokens, "Z");
                    if (path is null)
                        return false;

                    var existing = schema.GetTable(path.Value.Database, path.Value.Schema, path.Value.Name);
                    if (existing?.Columns is { Count: > 0 }
                        && items.Any(i => i.Kind == CompletionKind.Column))
                    {
                        return false;
                    }

                    schema.AddTable(new TableInfo(path.Value.Name, path.Value.Schema, path.Value.Database,
                        Columns: [new ColumnInfo("ACCOUNTKEY"), new ColumnInfo("ACCOUNTNAME")]));
                    return true;
                },
            });

        var labels = result.EngineItems.Select(item => item.Label).ToArray();
        Assert.Contains("TWO", labels, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ACCOUNTKEY", labels, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ACCOUNTNAME", labels, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void HavingBareScope_OffersSelectOutputAliases()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("CUSTOMERS", "SALES", "JUST_DATA",
            Columns: [new ColumnInfo("CUSTOMER_ID"), new ColumnInfo("EMAIL")]));

        var labels = Labels(schema,
            "SELECT CUSTOMER_ID, COUNT(*) AS N FROM JUST_DATA.SALES.CUSTOMERS GROUP BY CUSTOMER_ID HAVING |");

        Assert.Contains("N", labels, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("CUSTOMER_ID", labels, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void JoinUsingParens_OffersOnlyCommonColumns()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("CUSTOMERS", "SALES", "JUST_DATA",
            Columns: [new ColumnInfo("CUSTOMER_ID"), new ColumnInfo("EMAIL")]));
        schema.AddTable(new TableInfo("ORDERS", "SALES", "JUST_DATA",
            Columns: [new ColumnInfo("ORDER_ID"), new ColumnInfo("CUSTOMER_ID")]));

        var labels = Labels(schema,
            "SELECT * FROM JUST_DATA.SALES.CUSTOMERS C JOIN JUST_DATA.SALES.ORDERS O USING (|)");

        Assert.Contains("CUSTOMER_ID", labels, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("EMAIL", labels, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("ORDER_ID", labels, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void JoinUsingParens_DoesNotLeakIntoALaterParenGroup()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("CUSTOMERS", "SALES", "JUST_DATA",
            Columns: [new ColumnInfo("CUSTOMER_ID"), new ColumnInfo("EMAIL")]));
        schema.AddTable(new TableInfo("ORDERS", "SALES", "JUST_DATA",
            Columns: [new ColumnInfo("ORDER_ID"), new ColumnInfo("CUSTOMER_ID")]));

        // The open `(` belongs to a later join target, not to `USING (...)`, so
        // the join-key intersection must not replace the normal suggestions.
        var labels = Labels(schema,
            "SELECT * FROM JUST_DATA.SALES.CUSTOMERS C JOIN JUST_DATA.SALES.ORDERS O USING (CUSTOMER_ID) JOIN JUST_DATA.SALES.ORDERS O2 (|");

        Assert.DoesNotContain("CUSTOMER_ID", labels, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void AstScope_RegistersAliasedCteColumns()
    {
        var schema = new ExplicitQualifierSchemaProvider();
        schema.AddTable(new TableInfo("DIMDATE", "PUBLIC", "JUST_DATA",
            Columns: [new ColumnInfo("DATEKEY"), new ColumnInfo("CALENDARQUARTER")]));

        const string sql = "WITH XXX AS (SELECT * FROM JUST_DATA..DIMDATE) SELECT * FROM XXX AS Y WHERE Y.";
        var scope = new CompletionScopeProvider(schema).TryBuild(sql);

        Assert.NotNull(scope);
        var aliased = scope!.FindTable("Y");
        Assert.NotNull(aliased);
        Assert.NotNull(aliased!.Columns);
        Assert.Contains(aliased.Columns!, column => column.Name == "DATEKEY");
    }
}
