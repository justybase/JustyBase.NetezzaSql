using JustyBase.NetezzaSqlParser.Linter;

namespace JustyBase.NetezzaSql.Tests;

/// <summary>
/// Port of src/__tests__/extensions/mssql/mssqlQualityRules.test.ts.
/// </summary>
public sealed class MssqlLintRulesTests
{
    private static QualityRuleRegistry Registry()
    {
        var registry = new QualityRuleRegistry(MssqlLintRules.AllRules);
        return registry;
    }

    private static IReadOnlyList<string> Ids(string sql) =>
        Registry().AllRules.SelectMany(r => r.Check(sql)).Select(i => i.RuleId).ToList();

    [Fact]
    public void Registry_ContainsMSS001_Through_MSS008()
    {
        Assert.Equal(
            ["MSS001", "MSS002", "MSS003", "MSS004", "MSS005", "MSS006", "MSS007", "MSS008"],
            MssqlLintRules.AllRules.Select(r => r.Id).ToArray());
    }

    [Fact]
    public void MSS001_FlagsSelectStar()
    {
        Assert.Contains("MSS001", Ids("SELECT * FROM dbo.T"));
        Assert.Contains("MSS001", Ids("SELECT TOP 10 * FROM dbo.T"));
        Assert.Contains("MSS001", Ids("SELECT TOP (10) * FROM dbo.T"));
        Assert.DoesNotContain("MSS001", Ids("SELECT id FROM dbo.T"));
    }

    [Fact]
    public void MSS001_DoesNotFlagStarInsideFunction()
    {
        Assert.DoesNotContain("MSS001", Ids("SELECT COUNT(*) FROM dbo.T"));
    }

    [Fact]
    public void MSS002_MSS003_FlagDeleteUpdateWithoutWhere()
    {
        Assert.Contains("MSS002", Ids("DELETE FROM dbo.T"));
        Assert.Contains("MSS003", Ids("UPDATE dbo.T SET X = 1"));
        Assert.DoesNotContain("MSS002", Ids("DELETE FROM dbo.T WHERE id = 1"));
        Assert.DoesNotContain("MSS003", Ids("UPDATE dbo.T SET X = 1 WHERE id = 1"));
    }

    [Fact]
    public void MSS002_MultipartBracketedTable()
    {
        Assert.Contains("MSS002", Ids("""DELETE FROM [Sales].[Order Items] """));
        Assert.DoesNotContain("MSS002", Ids("""DELETE FROM [Sales].[Order Items] WHERE [Status] = 1"""));
    }

    [Fact]
    public void MSS004_MSS005_MSS007_RejectNetezzaCarryOvers()
    {
        Assert.Contains("MSS004", Ids("GROOM TABLE T"));
        Assert.Contains("MSS005", Ids("CREATE TABLE T (A INT) DISTRIBUTE ON (A)"));
        Assert.Contains("MSS007", Ids("SELECT * FROM T LIMIT 10"));
    }

    [Fact]
    public void MSS006_TopWithoutOrderBy()
    {
        Assert.Contains("MSS006", Ids("SELECT TOP 10 Id FROM dbo.T"));
        Assert.DoesNotContain("MSS006", Ids("SELECT TOP 10 Id FROM dbo.T ORDER BY Id"));
    }

    [Fact]
    public void MSS006_OffsetWithoutOrderBy()
    {
        Assert.Contains("MSS006", Ids("SELECT Id FROM dbo.T OFFSET 2 ROWS FETCH NEXT 5 ROWS ONLY"));
        Assert.DoesNotContain("MSS006", Ids("SELECT Id FROM dbo.T ORDER BY Id OFFSET 2 ROWS FETCH NEXT 5 ROWS ONLY"));
    }

    [Fact]
    public void MSS006_DoesNotThrowWhenStatementStartsAtColumnZero()
    {
        Assert.Contains("MSS006", Ids("TOP 10 Id FROM dbo.T"));
        Assert.Contains("MSS006", Ids("OFFSET 5 ROWS"));
    }

    [Fact]
    public void MSS008_FlagsDoubleDot()
    {
        Assert.Contains("MSS008", Ids("SELECT * FROM DB..TABLE"));
    }

    [Fact]
    public void CombinedDestructiveAndCarryOvers()
    {
        var ids = Ids(
            "DELETE FROM ORDERS; UPDATE ORDERS SET STATUS = 1; SELECT * FROM ORDERS; SELECT TOP 10 * FROM ORDERS; SELECT * FROM T LIMIT 5; SELECT * FROM DB..T;");
        Assert.Contains("MSS001", ids);
        Assert.Contains("MSS002", ids);
        Assert.Contains("MSS003", ids);
        Assert.Contains("MSS006", ids);
        Assert.Contains("MSS007", ids);
        Assert.Contains("MSS008", ids);
    }
}
