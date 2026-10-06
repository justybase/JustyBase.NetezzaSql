using JustyBase.NetezzaSqlLsp.Protocol;
using JustyBase.NetezzaSqlLsp.Services;
using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.NetezzaSql.Tests;

public sealed class CompletionRankingTests
{
    private static InMemorySchemaProvider CreateSchema()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("EMPLOYEES", "PUBLIC", "TESTDB", Columns:
        [
            new ColumnInfo("EMPLOYEE_ID", DataType: "INT4"),
            new ColumnInfo("FIRST_NAME", DataType: "VARCHAR(50)"),
            new ColumnInfo("LAST_NAME", DataType: "VARCHAR(50)")
        ]));
        return schema;
    }

    [Theory]
    [InlineData("employee_id", "EMPLOYEE_ID", CompletionMatchScorer.Exact)]
    [InlineData("EMP", "EMPLOYEE_ID", CompletionMatchScorer.Prefix)]
    [InlineData("empid", "EMP_ID", CompletionMatchScorer.CompactPrefix)]
    [InlineData("id", "EMPLOYEE_ID", CompletionMatchScorer.WordStart)]
    [InlineData("ei", "EMPLOYEE_ID", CompletionMatchScorer.Initials)]
    [InlineData("eid", "EMPLOYEE_ID", CompletionMatchScorer.Fragment)]
    public void MatchScorer_ClassifiesTiers(string partial, string label, int expected)
    {
        Assert.Equal(expected, CompletionMatchScorer.Compute(label, partial));
    }

    [Fact]
    public void MatchScorer_NoMatch_ReturnsZero()
    {
        Assert.Equal(0, CompletionMatchScorer.Compute("EMPLOYEE_ID", "zzz"));
        Assert.Equal(0, CompletionMatchScorer.Compute("XEMPLOYEE_ID", "e"));
    }

    [Fact]
    public async Task Orchestrator_FragmentMatch_FindsCompactLabel()
    {
        var text = "SELECT EMPLOYEE_ID FROM EMPLOYEES WHERE EID";

        var result = await CompletionOrchestrator.GetCompletions(
            text,
            text.Length,
            CreateSchema(),
            SqlDialect.Netezza);

        Assert.Contains(result.EngineItems, item => item.Label == "EMPLOYEE_ID");
    }

    [Fact]
    public async Task Orchestrator_PrefixMatch_ExcludesFuzzyTiers()
    {
        var text = "SELECT EMPLOYEE_ID FROM EMPLOYEES WHERE EMP";

        var result = await CompletionOrchestrator.GetCompletions(
            text,
            text.Length,
            CreateSchema(),
            SqlDialect.Netezza);

        Assert.Contains(result.EngineItems, item => item.Label == "EMPLOYEE_ID");
        Assert.DoesNotContain(
            result.EngineItems,
            item => item.Kind == CompletionKind.Column && item.Label == "FIRST_NAME");
    }

    [Fact]
    public async Task CompletionService_AssignsTierSortText()
    {
        const string text = "SELECT EMPLOYEE_ID FROM EMPLOYEES WHERE ";

        var list = await CompletionService.GetCompletions(
            text,
            line: 0,
            character: text.Length,
            CreateSchema(),
            SqlDialect.Netezza,
            triggerKind: (int)CompletionTriggerKind.Invoked);

        var column = Assert.Single(list.Items!, item => item.Label == "EMPLOYEE_ID");
        Assert.StartsWith("2_", column.SortText, StringComparison.Ordinal);
        Assert.Contains(list.Items!, item => item.SortText != null && item.SortText.StartsWith("5_", StringComparison.Ordinal));
    }

    [Fact]
    public void Engine_VariableCompletion_BuiltInAmpersand()
    {
        var engine = new NzCompletionEngine(schema: null, parsingCoordinator: null, catalog: null, dialect: SqlDialect.Netezza);

        var items = engine.GetCompletions("SELECT &RO", 10);

        Assert.Contains(items, item => item.Label == "&ROWCOUNT");
    }

    [Fact]
    public void Engine_VariableCompletion_BracedForm()
    {
        var engine = new NzCompletionEngine(schema: null, parsingCoordinator: null, catalog: null, dialect: SqlDialect.Netezza);

        var items = engine.GetCompletions("SELECT ${RO", 11);

        Assert.Contains(items, item => item.Label == "${ROWCOUNT}");
    }

    [Fact]
    public void Engine_VariableCompletion_DeclaredProcedureVariable()
    {
        const string sql =
            "CREATE OR REPLACE PROCEDURE P() RETURNS INT4 LANGUAGE NZPLSQL AS BEGIN_PROC " +
            "DECLARE v_total INT4; BEGIN SELECT &v_ FROM T; END; END_PROC;";
        var cursor = sql.IndexOf("&v_", StringComparison.Ordinal) + 3;
        var engine = new NzCompletionEngine(schema: null, parsingCoordinator: null, catalog: null, dialect: SqlDialect.Netezza);

        var items = engine.GetCompletions(sql, cursor);

        Assert.Contains(items, item => item.Label == "&v_total");
    }

    [Fact]
    public void Engine_VariableCompletion_DoesNotTreatDdlColumnAsVariable()
    {
        const string sql = "CREATE TABLE t (id INT4); SELECT &i";
        var engine = new NzCompletionEngine(schema: null, parsingCoordinator: null, catalog: null, dialect: SqlDialect.Netezza);

        var items = engine.GetCompletions(sql, sql.Length);

        Assert.DoesNotContain(items, item => item.Label == "&id");
    }

    [Fact]
    public void Engine_VariableCompletion_DoesNotLeakAfterProcedureEnd()
    {
        const string sql = "CREATE PROCEDURE P() RETURNS INT4 LANGUAGE NZPLSQL AS BEGIN_PROC DECLARE v_total INT4; BEGIN RETURN 1; END; END_PROC; SELECT &v_";
        var engine = new NzCompletionEngine(schema: null, parsingCoordinator: null, catalog: null, dialect: SqlDialect.Netezza);

        var items = engine.GetCompletions(sql, sql.Length);

        Assert.DoesNotContain(items, item => item.Label == "&v_total");
    }

    [Fact]
    public void Engine_NoVariableSigil_DoesNotUseVariableCompletion()
    {
        var engine = new NzCompletionEngine(schema: null, parsingCoordinator: null, catalog: null, dialect: SqlDialect.Netezza);

        var items = engine.GetCompletions("SELECT RO", 9);

        Assert.DoesNotContain(items, item => item.Label == "&ROWCOUNT");
    }
}
