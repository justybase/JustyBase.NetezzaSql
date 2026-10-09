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

    [Fact]
    public void TempTableFromEarlierStatement_RemainsVisible()
    {
        var labels = Labels("CREATE TEMP TABLE TT AS SELECT ORDER_ID FROM ORDERS;\nSELECT TT.| FROM TT");
        Assert.Contains("ORDER_ID", labels, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TempTableDroppedBeforeCaret_IsNoLongerVisible()
    {
        var labels = Labels("CREATE TEMP TABLE TT AS SELECT ORDER_ID FROM ORDERS;\nDROP TABLE TT;\nSELECT TT.| FROM TT");
        Assert.DoesNotContain("ORDER_ID", labels, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TempTableDroppedAfterCaret_StaysVisibleAtCaret()
    {
        var labels = Labels("CREATE TEMP TABLE TT AS SELECT ORDER_ID FROM ORDERS;\nSELECT TT.| FROM TT;\nDROP TABLE TT;");
        Assert.Contains("ORDER_ID", labels, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void RecreatedTempTable_UsesTheLatestDefinitionBeforeCaret()
    {
        var labels = Labels("CREATE TEMP TABLE TT AS SELECT ORDER_ID FROM ORDERS;\nDROP TABLE TT;\n"
            + "CREATE TEMP TABLE TT AS SELECT CUSTOMER_NAME FROM CUSTOMERS;\nSELECT TT.| FROM TT");
        Assert.Contains("CUSTOMER_NAME", labels, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("ORDER_ID", labels, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TempTableWithColumnDefinitions_OffersItsColumns()
    {
        var labels = Labels("CREATE TEMP TABLE TT (TT_ID INTEGER, TT_NAME VARCHAR(20));\nSELECT TT.| FROM TT");
        Assert.Contains("TT_ID", labels, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("TT_NAME", labels, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TempTableCreatedAfterCaret_IsNotVisibleYet()
    {
        var labels = Labels("SELECT TT.| FROM TT;\nCREATE TEMP TABLE TT AS SELECT ORDER_ID FROM ORDERS;");
        Assert.DoesNotContain("ORDER_ID", labels, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TempTableVisible_ButEarlierAliasStillDoesNotLeak()
    {
        var labels = Labels("SELECT * FROM CUSTOMERS C;\nCREATE TEMP TABLE TT AS SELECT ORDER_ID FROM ORDERS;\nSELECT C.| FROM TT");
        Assert.DoesNotContain("CUSTOMER_NAME", labels, StringComparer.OrdinalIgnoreCase);
    }
}
