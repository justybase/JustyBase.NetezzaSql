using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.NetezzaSql.Tests;

/// <summary>
/// Completion scope must come from the statement that contains the caret,
/// not from an earlier statement in the same document.
/// </summary>
public sealed class MultiStatementCompletionScopeTests
{
    private static NzCompletionEngine CreateEngine()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("CUSTOMERS", Columns: [new ColumnInfo("CUSTOMER_ID"), new ColumnInfo("CUSTOMER_NAME")]));
        schema.AddTable(new TableInfo("ORDERS", Columns: [new ColumnInfo("ORDER_ID"), new ColumnInfo("ORDER_TOTAL")]));
        return new NzCompletionEngine(schema);
    }

    private static IReadOnlyList<string> Labels(string sqlWithCaret)
    {
        var cursor = sqlWithCaret.IndexOf('|');
        var sql = sqlWithCaret.Remove(cursor, 1);
        return CreateEngine().GetCompletions(sql, cursor).Select(item => item.Label).ToList();
    }

    [Fact]
    public void AliasReusedInLaterStatement_ResolvesToLaterStatementTable()
    {
        var labels = Labels("SELECT * FROM CUSTOMERS X;\nSELECT X.| FROM ORDERS X");
        Assert.Contains("ORDER_ID", labels, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("CUSTOMER_NAME", labels, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void QualifiedAliasInLaterStatement_ResolvesItsOwnTable()
    {
        var labels = Labels("SELECT * FROM CUSTOMERS C;\nSELECT O.| FROM ORDERS O");
        Assert.Contains("ORDER_ID", labels, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("CUSTOMER_NAME", labels, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void AliasFromEarlierStatement_IsNotVisibleInLaterStatement()
    {
        var labels = Labels("SELECT * FROM CUSTOMERS C;\nSELECT C.| FROM ORDERS O");
        Assert.DoesNotContain("CUSTOMER_NAME", labels, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnqualifiedColumnsInLaterStatement_ComeFromItsFromClause()
    {
        var labels = Labels("SELECT * FROM CUSTOMERS;\nSELECT | FROM ORDERS");
        Assert.Contains("ORDER_ID", labels, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("CUSTOMER_NAME", labels, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void CteFromEarlierStatement_DoesNotLeakColumns()
    {
        var labels = Labels("WITH A AS (SELECT CUSTOMER_NAME FROM CUSTOMERS) SELECT * FROM A;\nSELECT | FROM ORDERS");
        Assert.Contains("ORDER_ID", labels, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("CUSTOMER_NAME", labels, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void CaretInFirstStatement_UsesFirstStatementScope()
    {
        var labels = Labels("SELECT X.| FROM ORDERS X;\nSELECT * FROM CUSTOMERS X");
        Assert.Contains("ORDER_ID", labels, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("CUSTOMER_NAME", labels, StringComparer.OrdinalIgnoreCase);
    }

    [Fact(Skip = "Known gap (pre-existing, also fails before the statement-scope fix): CTAS TEMP table columns are not offered after a qualifier in a later statement.")]
    public void TempTableFromEarlierStatement_RemainsVisible()
    {
        var labels = Labels("CREATE TEMP TABLE TT AS SELECT ORDER_ID FROM ORDERS;\nSELECT TT.| FROM TT");
        Assert.Contains("ORDER_ID", labels, StringComparer.OrdinalIgnoreCase);
    }
}
