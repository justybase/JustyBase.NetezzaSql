using JustyBase.NetezzaSqlParser.Linter;

namespace JustyBase.NetezzaSql.Tests;

public sealed class ProcedureRuleConformanceTests
{
    [Theory]
    [InlineData("NZP007", "SELECT 1\nRETURN 1;")]
    [InlineData("NZP011", "SELECT 1;")]
    [InlineData("NZP013", "IF p_id = 1\nRETURN 1;\nEND IF;")]
    [InlineData("NZP014", "LOOP\nEXIT;\nEND LOOP;\nRETURN 1;")]
    [InlineData("NZP015", "RETURN 1;", "value INTEGER")]
    [InlineData("NZP016", "DECLARE\ncounter INTEGER;\nBEGIN\ncounter := 1;\nRETURN counter;\nEND;")]
    [InlineData("NZP017", "SELECT CASE WHEN 1 = 1 THEN 1;\nRETURN 1;")]
    [InlineData("NZP018", "EXECUTE IMMEDIATE 'SELECT * FROM ' || p_table;\nRETURN 1;", "p_table VARCHAR")]
    [InlineData("NZP020", "v_sql := VARCHAR || 100;\nRETURN 1;")]
    [InlineData("NZP022", "RETURN 1;", "OUT out_value INTEGER")]
    [InlineData("NZP025", "COMMIT;\nRETURN 1;")]
    [InlineData("NZP027", "RETURN 1;", "p_id INTEGER", "")]
    [InlineData("NZP028", "DECLARE\nv_arr VARRAY;\nBEGIN\nv_arr(1) := 10;\nRETURN 1;\nEND;")]
    [InlineData("NZP029", "BEGIN\nBEGIN\nBEGIN\nBEGIN\nNULL;\nEND;\nEND;\nEND;\nEND;\nRETURN 1;")]
    public void ProcedureRule_ReportsReferencePositiveCase(string id, string body, string parameters = "p_id INTEGER", string executeAs = "CALLER")
    {
        using var engine = new LintEngine();
        var rule = engine.Registry.GetRule(id);
        Assert.NotNull(rule);

        var executeLine = string.IsNullOrEmpty(executeAs) ? string.Empty : "EXECUTE AS " + executeAs + "\n";
        var sql = "CREATE OR REPLACE PROCEDURE test_proc(" + parameters + ")\n" +
            executeLine + "RETURNS INT4\nLANGUAGE NZPLSQL AS\nBEGIN_PROC\n" + body + "\nEND_PROC;@@@";

        Assert.NotEmpty(rule!.Check(sql));
    }

    // Retired with live evidence: DEFAULT arguments, explicit cursors, a
    // missing RETURN, PERFORM and SQLSTATE handlers are either rejected by
    // Netezza or accepted without the problem the rule described. The IDs stay
    // registered so ID parity with the TypeScript engine is unchanged.
    [Theory]
    [InlineData("NZP019", "RETURN 1;", "p_id INTEGER, p_name VARCHAR")]
    [InlineData("NZP023", "OPEN c;\nRETURN 1;")]
    [InlineData("NZP024", "NULL;")]
    [InlineData("NZP026", "SELECT do_work();\nRETURN 1;")]
    [InlineData("NZP030", "BEGIN\nNULL;\nEXCEPTION\nWHEN SQLSTATE '02000' THEN\nRETURN 0;\nEND;")]
    public void ProcedureRule_RetiredRulesStaySilent(string id, string body, string parameters = "p_id INTEGER")
    {
        using var engine = new LintEngine();
        var rule = engine.Registry.GetRule(id);
        Assert.NotNull(rule);

        var sql = "CREATE OR REPLACE PROCEDURE test_proc(" + parameters + ")\n" +
            "RETURNS INT4\nLANGUAGE NZPLSQL AS\nBEGIN_PROC\n" + body + "\nEND_PROC;";

        Assert.Empty(rule!.Check(sql));
    }

    [Fact]
    public void ProcedureRule_LeavesSafeCasesUnreported()    {
        using var engine = new LintEngine();
        var safe = "CREATE OR REPLACE PROCEDURE test_proc(p_value INTEGER DEFAULT 0) EXECUTE AS CALLER " +
            "RETURNS INT4 LANGUAGE NZPLSQL AS BEGIN_PROC RETURN p_value; END_PROC;";

        foreach (var id in new[] { "NZP014", "NZP015", "NZP016", "NZP018", "NZP019", "NZP025", "NZP026", "NZP027", "NZP028", "NZP029" })
        {
            var rule = engine.Registry.GetRule(id);
            Assert.NotNull(rule);
            Assert.Empty(rule!.Check(safe));
        }
    }
}
