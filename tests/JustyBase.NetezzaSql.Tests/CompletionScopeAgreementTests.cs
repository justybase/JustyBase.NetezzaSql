using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Completion;
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
}
