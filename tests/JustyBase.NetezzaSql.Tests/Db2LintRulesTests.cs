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
    public void Registry_ContainsDB2L001_Through_DB2L008()
    {
        Assert.Equal(
            ["DB2L001", "DB2L002", "DB2L003", "DB2L004", "DB2L005", "DB2L006", "DB2L007", "DB2L008"],
            Db2LintRules.AllRules.Select(r => r.Id).ToArray());
    }

    [Fact]
    public void AvaloniaRulesDoNotReuseRustDb2001ToDb2004DiagnosticCodes()
    {
        string[] rustLanguageCodes = ["DB2001", "DB2002", "DB2003", "DB2004"];

        Assert.DoesNotContain(Db2LintRules.AllRules, rule => rustLanguageCodes.Contains(rule.Id));
        Assert.All(Db2LintRules.AllRules, rule => Assert.StartsWith("DB2L", rule.Id));
    }

    [Fact]
    public void DB2L001_FlagsSelectStar()
    {
        Assert.Contains("DB2L001", Ids("SELECT * FROM T"));
    }

    [Fact]
    public void DB2L002_DB2L003_FlagDeleteUpdateWithoutWhere()
    {
        Assert.Contains("DB2L002", Ids("DELETE FROM T"));
        Assert.Contains("DB2L003", Ids("UPDATE T SET X = 1"));
    }

    [Fact]
    public void DB2L002_MultipartQuotedTable()
    {
        Assert.Contains("DB2L002", Ids("""DELETE FROM "SCHEMA"."TABLE" """));
        Assert.DoesNotContain("DB2L002", Ids("""DELETE FROM "SCHEMA"."TABLE" WHERE 1=1"""));
    }

    [Fact]
    public void DB2L004_DB2L005_RejectNetezzaCarryOvers()
    {
        Assert.Contains("DB2L004", Ids("GROOM TABLE T"));
        Assert.Contains("DB2L005", Ids("CREATE TABLE T (A INT) DISTRIBUTE ON (A)"));
    }

    [Fact]
    public void DB2L006_TopNWithoutOrderBy()
    {
        Assert.Contains("DB2L006", Ids("SELECT * FROM T FETCH FIRST 5 ROWS ONLY"));
        Assert.DoesNotContain("DB2L006", Ids("SELECT * FROM T ORDER BY ID FETCH FIRST 5 ROWS ONLY"));
    }

    [Fact]
    public void DB2L007_DB2L008_RejectLimitAndDoubleDot()
    {
        Assert.Contains("DB2L007", Ids("SELECT * FROM T LIMIT 10"));
        Assert.Contains("DB2L008", Ids("SELECT * FROM DB..TABLE"));
    }

    [Fact]
    public void CombinedDestructiveAndCarryOvers()
    {
        var ids = Ids(
            "DELETE FROM ORDERS; UPDATE ORDERS SET STATUS = 1; SELECT * FROM ORDERS FETCH FIRST 10 ROWS ONLY; SELECT * FROM T LIMIT 5; SELECT * FROM DB..T;");
        Assert.Contains("DB2L001", ids);
        Assert.Contains("DB2L002", ids);
        Assert.Contains("DB2L003", ids);
        Assert.Contains("DB2L006", ids);
        Assert.Contains("DB2L007", ids);
        Assert.Contains("DB2L008", ids);
    }
}
