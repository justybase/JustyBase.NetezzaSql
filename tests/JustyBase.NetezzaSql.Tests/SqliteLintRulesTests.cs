using JustyBase.NetezzaSqlParser.Linter;

namespace JustyBase.NetezzaSql.Tests;

public sealed class SqliteLintRulesTests
{
    [Fact]
    public void Autoincrement_RequiresIntegerPrimaryKey()
    {
        var issues = new RuleSqlite001_AutoincrementRequiresIntegerPk().Check(
            "CREATE TABLE t (id INTEGER PRIMARY KEY AUTOINCREMENT); CREATE TABLE u (id TEXT PRIMARY KEY AUTOINCREMENT)");
        Assert.Single(issues);
        Assert.Equal("SQLITE001", issues.First().RuleId);
    }

    [Fact]
    public void Autoincrement_IsColumnScoped()
    {
        // A valid INTEGER PRIMARY KEY elsewhere in the table must not silence
        // an AUTOINCREMENT on a non-INTEGER column.
        var issues = new RuleSqlite001_AutoincrementRequiresIntegerPk().Check(
            "CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT PRIMARY KEY AUTOINCREMENT)");
        Assert.Single(issues);
    }

    [Fact]
    public void Autoincrement_InsideStringIsIgnored()
    {
        var issues = new RuleSqlite001_AutoincrementRequiresIntegerPk().Check(
            "SELECT 'AUTOINCREMENT is not syntax'");
        Assert.Empty(issues);
    }

    [Fact]
    public void WithoutRowid_RequiresPrimaryKey()
    {
        var issues = new RuleSqlite002_WithoutRowidRequiresPrimaryKey().Check(
            "CREATE TABLE a (id INTEGER PRIMARY KEY) WITHOUT ROWID; CREATE TABLE b (v TEXT) WITHOUT ROWID");
        Assert.Single(issues);
        Assert.Equal("SQLITE002", issues.First().RuleId);
    }

    [Fact]
    public void CreateSequence_IsReported()
    {
        var issues = new RuleSqlite003_CreateSequence().Check("CREATE SEQUENCE seq1; CREATE TABLE t (id SERIAL)");
        Assert.Equal(2, issues.Count());
        Assert.All(issues, issue => Assert.Equal("SQLITE003", issue.RuleId));
    }

    [Theory]
    [InlineData("SELECT NVL(a, 0) FROM t")]
    [InlineData("SELECT SYSDATE FROM t")]
    [InlineData("SELECT TO_CHAR(a, 'fmt') FROM t")]
    [InlineData("SELECT TO_DATE(a, 'fmt') FROM t")]
    public void LegacyFunctions_AreReported(string sql)
    {
        var issues = new RuleSqlite004_NonSqliteFunctions().Check(sql);
        Assert.Single(issues);
        Assert.Equal("SQLITE004", issues.First().RuleId);
    }

    [Fact]
    public void LegacyFunction_InsideStringIsIgnored()
    {
        var issues = new RuleSqlite004_NonSqliteFunctions().Check("SELECT 'SYSDATE() is text'");
        Assert.Empty(issues);
    }

    [Fact]
    public void AlterTableAddConstraint_IsReported()
    {
        var issues = new RuleSqlite005_AlterTableAddConstraint().Check(
            "ALTER TABLE t ADD COLUMN c INTEGER; ALTER TABLE t ADD CONSTRAINT fk FOREIGN KEY (c) REFERENCES u(id)");
        Assert.Single(issues);
        Assert.Equal("SQLITE005", issues.First().RuleId);
    }

    [Fact]
    public void Strict_IsReportedAsWarning()
    {
        var issues = new RuleSqlite006_StrictRequiresModernVersion().Check("CREATE TABLE t (a INTEGER) STRICT");
        var issue = Assert.Single(issues);
        Assert.Equal("SQLITE006", issue.RuleId);
        Assert.Equal(LintSeverity.Warning, issue.Severity);
    }

    [Fact]
    public void Registry_ContainsAllSqliteRules()
    {
        Assert.Equal(6, SqliteLintRules.AllRules.Count);
        Assert.Equal(
            ["SQLITE001", "SQLITE002", "SQLITE003", "SQLITE004", "SQLITE005", "SQLITE006"],
            SqliteLintRules.AllRules.Select(r => r.Id).ToArray());
    }
}
