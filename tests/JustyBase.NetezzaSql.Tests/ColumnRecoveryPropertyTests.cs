using System.Text.RegularExpressions;
using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.NetezzaSql.Tests;

/// <summary>
/// Property-style regression tests for column identity on incomplete SQL:
/// deterministic variants of seed queries must never throw, never produce an
/// empty occurrence, and never point at a catalog column the text does not name.
/// </summary>
public sealed partial class ColumnRecoveryPropertyTests
{
    private static readonly string[] Seeds =
    [
        "SELECT 'zażółć 😀' AS TXT, C.CUSTOMER_ID FROM JUST_DATA.SALES.CUSTOMERS C WHERE C.CUSTOMER_ID > 0",
        "SELECT C.CUSTOMER_ID,\r\n       C.EMAIL\r\nFROM JUST_DATA.SALES.CUSTOMERS C\r\nWHERE C.EMAIL IS NOT NULL",
        "WITH X AS (SELECT CUSTOMER_ID AS CID FROM JUST_DATA.SALES.CUSTOMERS) SELECT X.CID FROM X WHERE X.CID > 0",
        "SELECT D.CID, O.ORDER_DATE FROM (SELECT CUSTOMER_ID AS CID FROM JUST_DATA.SALES.CUSTOMERS) D JOIN JUST_DATA.SALES.ORDERS O ON O.CUSTOMER_ID = D.CID",
        "SELECT C.CUSTOMER_NAME FROM JUST_DATA.SALES.CUSTOMERS C; SELECT O.ORDER_ID FROM JUST_DATA.SALES.ORDERS O WHERE O.ORDER_ID > 1",
    ];

    private static readonly string[] NamedIncomplete =
    [
        "SELECT C.CUSTOMER_ID, FROM JUST_DATA.SALES.CUSTOMERS C WHERE C.CUSTOMER_ID > 0",
        "SELECT C.CUSTOMER_ID FROM JUST_DATA.SALES.CUSTOMERS C WHERE C.",
        "SELECT C.CUSTOMER_ID FROM JUST_DATA.SALES.CUSTOMERS C WHERE ",
        "SELECT C.CUSTOMER_ID FROM JUST_DATA.SALES.CUSTOMERS C WHERE C.CUSTOMER_ID =",
        "SELECT D.CID FROM (SELECT CUSTOMER_ID AS CID FROM JUST_DATA.SALES.CUSTOMERS D WHERE D.CID > 0",
        "WITH X AS (SELECT CUSTOMER_ID AS FROM JUST_DATA.SALES.CUSTOMERS) SELECT * FROM X",
        "WITH X AS (SELECT CUSTOMER_ID AS CID FROM JUST_DATA.SALES.CUSTOMERS SELECT X.CID FROM X",
        "SELECT CUSTOMER_ID FROM JUST_DATA.SALES.",
        "SELECT 'zażółć 😀' AS TXT, C.CUSTOMER_ID,\r\nFROM JUST_DATA.SALES.CUSTOMERS C\r\nWHERE C.",
    ];

    [GeneratedRegex("\"(?:[^\"]|\"\")*\"|'(?:[^']|'')*'|[\\p{L}_][\\p{L}\\p{N}_$#]*|\\d+|\\S")]
    private static partial Regex TokenPattern();

    private static InMemorySchemaProvider Schema()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("CUSTOMERS", "SALES", "JUST_DATA",
            Columns: [new ColumnInfo("CUSTOMER_ID"), new ColumnInfo("CUSTOMER_NAME"), new ColumnInfo("EMAIL")]));
        schema.AddTable(new TableInfo("ORDERS", "SALES", "JUST_DATA",
            Columns: [new ColumnInfo("ORDER_ID"), new ColumnInfo("CUSTOMER_ID"), new ColumnInfo("ORDER_DATE")]));
        return schema;
    }

    private static IEnumerable<string> Variants(string seed)
    {
        var variants = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match token in TokenPattern().Matches(seed))
        {
            var end = token.Index + token.Length;
            variants.Add(seed[..token.Index]);
            variants.Add(seed[..end]);
            variants.Add(seed[..token.Index] + seed[end..]);
            variants.Add(seed[..end] + "," + seed[end..]);
        }
        return variants;
    }

    private static HashSet<string> Words(string sql) =>
        TokenPattern().Matches(sql).Select(match => match.Value.Trim('"').ToUpperInvariant()).ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void IncompleteVariants_NeverThrow_NeverYieldEmptyOccurrences_NeverInventCatalogTargets()
    {
        var schema = Schema();
        var all = Seeds.SelectMany(Variants).Concat(NamedIncomplete).ToArray();
        Assert.True(all.Length > 300);
        foreach (var sql in all)
        {
            var analysis = NzColumnIdentityService.Analyze(sql, schema);
            if (analysis is null) continue;
            var words = Words(sql);
            for (var offset = 0; offset <= sql.Length; offset++)
            {
                var identity = analysis.Resolve(offset);
                if (identity is null) continue;
                Assert.False(string.IsNullOrEmpty(identity.Name), sql);
                foreach (var occurrence in identity.Occurrences)
                {
                    Assert.True(occurrence.EndOffset > occurrence.StartOffset, sql);
                    Assert.False(string.IsNullOrWhiteSpace(sql[occurrence.StartOffset..occurrence.EndOffset]), sql);
                }
                var target = analysis.GetCatalogTarget(offset);
                foreach (var (relation, column) in new[]
                         {
                             (identity.Catalog?.Relation, identity.Catalog?.Column),
                             (identity.Origin?.Relation, identity.Origin?.Column),
                             (target?.Relation, target?.Column),
                         })
                {
                    if (relation is null && column is null) continue;
                    Assert.True(!string.IsNullOrEmpty(relation) && words.Contains(relation.ToUpperInvariant()),
                        $"invented relation '{relation}' in: {sql}");
                    Assert.True(!string.IsNullOrEmpty(column) && words.Contains(column.ToUpperInvariant()),
                        $"invented column '{column}' in: {sql}");
                }
            }
        }
    }

    [Fact]
    public void OpaqueOrUnfinishedSources_HaveNoCatalogTarget()
    {
        var schema = Schema();
        foreach (var (sql, column) in new[]
                 {
                     ("SELECT CUSTOMER_ID FROM JUST_DATA.SALES.", "CUSTOMER_ID"),
                     ("SELECT C.CUSTOMER_ID FROM ", "CUSTOMER_ID"),
                     ("SELECT T.X FROM TABLE(F(1)) T", "X"),
                 })
        {
            var offset = sql.IndexOf(column, StringComparison.Ordinal);
            Assert.Null(NzColumnIdentityService.GetCatalogTarget(sql, offset, schema));
            Assert.Null(NzColumnIdentityService.Resolve(sql, offset, schema)?.Catalog);
        }
    }

    [Fact]
    public void ScopeDoesNotLeakAcrossStatementBoundary()
    {
        const string sql = "SELECT C.CUSTOMER_NAME, FROM JUST_DATA.SALES.CUSTOMERS C; SELECT C.ORDER_ID FROM JUST_DATA.SALES.ORDERS O";
        var analysis = NzColumnIdentityService.Analyze(sql, Schema());
        var second = analysis?.Resolve(sql.LastIndexOf("ORDER_ID", StringComparison.Ordinal));
        Assert.True(second is null || second.Status == SqlColumnResolutionStatus.Unresolved);
    }
}
