using JustyBase.NetezzaSqlParser.Linter;

namespace JustyBase.NetezzaSql.Tests;

/// <summary>
/// Port of src/__tests__/sqlParser/db2QualityRules.test.ts.
/// </summary>
public sealed class Db2LintRulesTests
{
    private static QualityRuleRegistry Registry()
    {
        var registry = new QualityRuleRegistry(Db2LintRules.AllRules);
        return registry;
    }

    private static IReadOnlyList<string> Ids(string sql) =>
        Registry().AllRules.SelectMany(r => r.Check(sql)).Select(i => i.RuleId).ToList();

    [Fact]
    public void Registry_ContainsDB2001_Through_DB2008()
    {
        Assert.Equal(
            ["DB2001", "DB2002", "DB2003", "DB2004", "DB2005", "DB2006", "DB2007", "DB2008"],
            Db2LintRules.AllRules.Select(r => r.Id).ToArray());
    }

    [Fact]
    public void DB2001_FlagsSelectStar()
    {
        Assert.Contains("DB2001", Ids("SELECT * FROM T"));
    }

    [Fact]
    public void DB2002_DB2003_FlagDeleteUpdateWithoutWhere()
    {
        Assert.Contains("DB2002", Ids("DELETE FROM T"));
        Assert.Contains("DB2003", Ids("UPDATE T SET X = 1"));
    }

    [Fact]
    public void DB2002_MultipartQuotedTable()
    {
        Assert.Contains("DB2002", Ids("""DELETE FROM "SCHEMA"."TABLE" """));
        Assert.DoesNotContain("DB2002", Ids("""DELETE FROM "SCHEMA"."TABLE" WHERE 1=1"""));
    }

    [Fact]
    public void DB2004_DB2005_RejectNetezzaCarryOvers()
    {
        Assert.Contains("DB2004", Ids("GROOM TABLE T"));
        Assert.Contains("DB2005", Ids("CREATE TABLE T (A INT) DISTRIBUTE ON (A)"));
    }

    [Fact]
    public void DB2006_TopNWithoutOrderBy()
    {
        Assert.Contains("DB2006", Ids("SELECT * FROM T FETCH FIRST 5 ROWS ONLY"));
        Assert.DoesNotContain("DB2006", Ids("SELECT * FROM T ORDER BY ID FETCH FIRST 5 ROWS ONLY"));
    }

    [Fact]
    public void DB2007_DB2008_RejectLimitAndDoubleDot()
    {
        Assert.Contains("DB2007", Ids("SELECT * FROM T LIMIT 10"));
        Assert.Contains("DB2008", Ids("SELECT * FROM DB..TABLE"));
    }

    [Fact]
    public void CombinedDestructiveAndCarryOvers()
    {
        var ids = Ids(
            "DELETE FROM ORDERS; UPDATE ORDERS SET STATUS = 1; SELECT * FROM ORDERS FETCH FIRST 10 ROWS ONLY; SELECT * FROM T LIMIT 5; SELECT * FROM DB..T;");
        Assert.Contains("DB2001", ids);
        Assert.Contains("DB2002", ids);
        Assert.Contains("DB2003", ids);
        Assert.Contains("DB2006", ids);
        Assert.Contains("DB2007", ids);
        Assert.Contains("DB2008", ids);
    }
}
