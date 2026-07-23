using JustyBase.NetezzaSqlParser.Linter;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.NetezzaSql.Tests;

public sealed class LinterConformanceTests
{
    [Theory]
    [InlineData("NZ001", "SELECT * FROM t;")]
    [InlineData("NZ002", "DELETE FROM t;")]
    [InlineData("NZ003", "UPDATE t SET c = 1;")]
    [InlineData("NZ004", "SELECT 1 FROM a CROSS JOIN b;")]
    [InlineData("NZ005", "SELECT 1 FROM t WHERE name LIKE '%x';")]
    [InlineData("NZ006", "SELECT 1 FROM t ORDER BY c;")]
    [InlineData("NZ007", "select * FROM t;")]
    [InlineData("NZ008", "TRUNCATE TABLE t;")]
    [InlineData("NZ009", "SELECT 1 FROM t WHERE a = 1 OR b = 2 OR c = 3;")]
    [InlineData("NZ010", "SELECT 1 FROM a JOIN b ON a.id = b.id;")]
    [InlineData("NZ011", "CREATE TABLE t AS SELECT 1;")]
    [InlineData("NZ012", "UPDATE t AS x SET c = 1;")]
    [InlineData("NZ013", "SELECT 1 UNION SELECT 2;")]
    [InlineData("NZ014", "SELECT 1 FROM a JOIN b ON a.id = b.id OR a.x = b.x;")]
    [InlineData("NZ015", "SELECT 1 FROM t WHERE LOWER(name) = 'x';")]
    [InlineData("NZ016", "SELECT 1 FROM a JOIN b ON a.id::INT = b.id;")]
    [InlineData("NZ017", "SELECT \"mixedName\" FROM t;")]
    [InlineData("NZ018", "SELECT 1 FROM orders o JOIN orders x ON o.id = o.id;")]
    [InlineData("NZ019", "SELECT CASE WHEN a = 1 THEN 2 FROM t;")]
    [InlineData("NZ020", "SELECT 1 FROM t WHERE id IN (SELECT id FROM x);")]
    [InlineData("NZ021", "SELECT a,, b FROM t;")]
    [InlineData("NZ022", "SELECT a WHERE a = 1;")]
    [InlineData("NZ023", "SELEC 1 FROM t;")]
    [InlineData("NZ024", "SELECT a, FROM t;")]
    public void CheapRule_ReportsItsReferenceTrigger(string id, string sql)
    {
        using var engine = new LintEngine();
        var rule = engine.Registry.GetRule(id);
        Assert.NotNull(rule);

        Assert.NotEmpty(rule!.Check(sql));
    }

    [Fact]
    public void Registry_ContainsAllReferenceNetezzaRuleIds()
    {
        using var engine = new LintEngine();
        var expected = Enumerable.Range(1, 24).Select(number => "NZ" + number.ToString("000"))
            .Concat(Enumerable.Range(101, 8).Select(number => "NZ" + number))
            .Concat(Enumerable.Range(1, 20).Select(number => "NZP" + number.ToString("000")))
            .Concat(Enumerable.Range(22, 9).Select(number => "NZP" + number.ToString("000")));

        foreach (var id in expected)
            Assert.True(engine.Registry.HasRule(id), "Missing rule " + id);
    }

    [Fact]
    public void EveryRegisteredRule_IsSafeOnReferenceSmokeCorpus()
    {
        using var engine = new LintEngine();
        using var runtime = new ParsingRuntime();
        var parse = runtime.Parse("CREATE OR REPLACE PROCEDURE p() RETURNS INTEGER LANGUAGE NZPLSQL AS BEGIN_PROC RETURN 1; END_PROC;");

        foreach (var rule in engine.Registry.AllRules)
        {
            _ = rule.Check("SELECT * FROM a CROSS JOIN b WHERE id LIKE '%x';").ToList();
            foreach (var statement in parse.Statements)
                _ = rule.CheckStatement(statement).ToList();
        }
    }

    [Fact]
    public void MissingReturnRule_FlagsProcedureWithReturnsButNoReturn()
    {
        var rule = new RuleNZP024_MissingReturn();
        var issue = Assert.Single(rule.Check(
            "CREATE PROCEDURE p() RETURNS INTEGER LANGUAGE NZPLSQL AS BEGIN_PROC NULL; END_PROC;"));

        Assert.Equal("NZP024", issue.RuleId);
    }

    [Fact]
    public void OrderByRule_IgnoresWindowOrderByButFlagsTopLevelOrderBy()
    {
        var rule = new RuleNZ006_OrderByWithoutLimit();

        Assert.Empty(rule.Check("SELECT ROW_NUMBER() OVER (ORDER BY id) FROM orders"));
        var issue = Assert.Single(rule.Check("SELECT id FROM orders ORDER BY id"));
        Assert.Equal("NZ006", issue.RuleId);
    }

    [Fact]
    public void RegexRules_IgnoreCommentsAndEscapedStringLiterals()
    {
        var crossJoin = new RuleNZ004_CrossJoin();

        Assert.Empty(crossJoin.Check("-- CROSS JOIN\nSELECT 'it''s not a CROSS JOIN'"));
    }

    [Fact]
    public void Engine_AppliesRegistrySeverityOverrides()
    {
        using var engine = new LintEngine();
        engine.Registry.SetSeverity("NZ001", RuleSeverityConfig.Error);

        var issue = Assert.Single(engine.RunCheapRules("SELECT * FROM orders"));
        Assert.Equal(LintSeverity.Error, issue.Severity);
        Assert.Equal("NZ001", issue.RuleId);
    }

    [Fact]
    public void Engine_DoesNotReportMissingRelationForTableBeingCreated()
    {
        using var engine = new LintEngine();
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new JustyBase.NetezzaSqlParser.Ast.TableInfo("EXISTING", "ADMIN", "JUST_DATA"));

        const string sql = "CREATE TABLE JUST_DATA.ADMIN.TMP_VXFEFNNOMV (COL_1_A INTEGER, COL_2_A INTEGER) DISTRIBUTE ON RANDOM";
        var result = engine.RunFullLint(new LintConfig(sql, schema, "create-table-regression"));

        Assert.DoesNotContain(result.Issues, issue => issue.RuleId == "SQL006");
    }

    [Fact]
    public void Engine_CarriesCreatedTablesIntoLaterStatements()
    {
        using var engine = new LintEngine();
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new JustyBase.NetezzaSqlParser.Ast.TableInfo("EXISTING", "ADMIN", "JUST_DATA"));

        const string sql = "CREATE TABLE JUST_DATA.ADMIN.TMP_SCRIPT_TABLE (ID INTEGER) DISTRIBUTE ON RANDOM; SELECT * FROM JUST_DATA.ADMIN.TMP_SCRIPT_TABLE";
        var result = engine.RunFullLint(new LintConfig(sql, schema, "script-scope-regression"));

        Assert.DoesNotContain(result.Issues, issue => issue.RuleId == "SQL006");
    }

    [Fact]
    public void Engine_RevalidatesFollowingStatementsWhenCreateTableChanges()
    {
        using var engine = new LintEngine();
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new JustyBase.NetezzaSqlParser.Ast.TableInfo("EXISTING", "ADMIN", "JUST_DATA"));

        const string uri = "script-scope-change-regression";
        var first = engine.RunFullLint(new LintConfig(
            "CREATE TABLE JUST_DATA.ADMIN.TMP_SCOPE_A (ID INTEGER); SELECT * FROM JUST_DATA.ADMIN.TMP_SCOPE_A",
            schema, uri));
        Assert.DoesNotContain(first.Issues, issue => issue.RuleId == "SQL006");

        var second = engine.RunFullLint(new LintConfig(
            "CREATE TABLE JUST_DATA.ADMIN.TMP_SCOPE_B (ID INTEGER); SELECT * FROM JUST_DATA.ADMIN.TMP_SCOPE_A",
            schema, uri));

        Assert.Contains(second.Issues, issue => issue.RuleId == "SQL006");
    }

    [Fact]
    public void Engine_RemovesStaleRelationDiagnosticWhenCreateTableMatchesReference()
    {
        using var engine = new LintEngine();
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new JustyBase.NetezzaSqlParser.Ast.TableInfo("EXISTING", "ADMIN", "JUST_DATA"));

        const string uri = "script-scope-create-regression";
        var first = engine.RunFullLint(new LintConfig(
            "CREATE TABLE JUST_DATA.ADMIN.TMP_SCOPE_B (ID INTEGER); SELECT * FROM JUST_DATA.ADMIN.TMP_SCOPE_A",
            schema, uri));
        Assert.Contains(first.Issues, issue => issue.RuleId == "SQL006");

        var second = engine.RunFullLint(new LintConfig(
            "CREATE TABLE JUST_DATA.ADMIN.TMP_SCOPE_A (ID INTEGER); SELECT * FROM JUST_DATA.ADMIN.TMP_SCOPE_A",
            schema, uri));

        Assert.DoesNotContain(second.Issues, issue => issue.RuleId == "SQL006");
    }

    [Fact]
    public void Engine_ReportsMissingUnqualifiedTable_WhenSchemaHasTables()
    {
        using var engine = new LintEngine();
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new JustyBase.NetezzaSqlParser.Ast.TableInfo("EXISTING", "ADMIN", "JUST_DATA"));

        const string sql = "SELECT * FROM NO_SUCH_TABLE";
        var result = engine.RunFullLint(new LintConfig(sql, schema, "unqualified-regression"));

        Assert.Contains(result.Issues, issue => issue.RuleId == "SQL006");
    }

    [Fact]
    public void Visitor_ReportsReferenceSemanticParityDiagnostics()
    {
        using var engine = new LintEngine();
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new JustyBase.NetezzaSqlParser.Ast.TableInfo(
            "T", Columns: new[]
            {
                new JustyBase.NetezzaSqlParser.Ast.ColumnInfo("TEXT_COL", DataType: "VARCHAR"),
                new JustyBase.NetezzaSqlParser.Ast.ColumnInfo("NUM_COL", DataType: "INTEGER")
            }));

        var result = engine.RunFullLint(new LintConfig(
            "SELECT TEXT_COL > 1 FROM T; SELECT * FROM T JOIN T2; INSERT INTO T (TEXT_COL, NUM_COL) VALUES ('x')",
            schema, "semantic-parity-regression"));

        Assert.Contains(result.Issues, issue => issue.RuleId == "SQL026");
        Assert.Contains(result.Issues, issue => issue.RuleId == "SQL027");
        Assert.Contains(result.Issues, issue => issue.RuleId == "SQL029");
    }

    [Fact]
    public void Visitor_ReportsDuplicateProjectionAndCteColumns()
    {
        using var engine = new LintEngine();
        var result = engine.RunFullLint(new LintConfig(
            "WITH c(a, a) AS (SELECT 1, 2) SELECT 1 AS x, 2 AS x",
            new InMemorySchemaProvider(), "duplicate-output-regression"));

        Assert.Contains(result.Issues, issue => issue.RuleId == "SQL023");
        Assert.Contains(result.Issues, issue => issue.RuleId == "SQL049");
    }

    [Fact]
    public void ProcedureScope_ReportsReturnAndAssignmentDiagnostics()
    {
        using var engine = new LintEngine();
        var result = engine.RunFullLint(new LintConfig(
            "CREATE PROCEDURE p(OUT out_param INTEGER) RETURNS INTEGER LANGUAGE NZPLSQL AS BEGIN_PROC SELECT 1; END_PROC",
            new InMemorySchemaProvider(), "procedure-scope-regression"));

        Assert.Contains(result.Issues, issue => issue.RuleId == "SQL037");
        Assert.Contains(result.Issues, issue => issue.RuleId == "SQL038");
        Assert.Contains(result.Issues, issue => issue.RuleId == "SQL040");
    }

    [Fact]
    public void ParserDiagnostics_ReportLexerAndDuplicateClauseErrors()
    {
        using var engine = new LintEngine();
        var schema = new InMemorySchemaProvider();

        var duplicate = engine.RunFullLint(new LintConfig(
            "SELECT 1 FROM FROM T", schema, "parser-duplicate-regression"));
        Assert.Contains(duplicate.Issues, issue => issue.RuleId == "PAR003");

        var lexer = engine.RunFullLint(new LintConfig(
            "SELECT 1 §", schema, "parser-lexer-regression"));
        Assert.Contains(lexer.Issues, issue => issue.RuleId == "LEX001");
    }

    [Fact]
    public void Visitor_ReportsTableQualificationProposal()
    {
        using var engine = new LintEngine();
        var schema = new InMemorySchemaProvider();
        schema.SetTableQualificationProposals(new[]
        {
            new TableQualificationProposal("DB1", "PUBLIC", "EMPLOYEES", "DB1.PUBLIC.EMPLOYEES", true)
        });

        var result = engine.RunFullLint(new LintConfig(
            "SELECT * FROM EMPLOYEES", schema, "qualification-regression"));

        var issue = Assert.Single(result.Issues, item => item.RuleId == "SQL048");
        Assert.Equal(LintSeverity.Information, issue.Severity);
        Assert.Equal("DB1.PUBLIC.EMPLOYEES", issue.SuggestedFix);
    }
}
