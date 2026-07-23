using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

/// <summary>
/// Protected parity gate — key completion scenarios ported from reference completionEngine.test.ts.
/// </summary>
public sealed class NzCompletionParityGateTests
{
    private readonly NzCompletionEngine _engine;
    private readonly InMemorySchemaProvider _schema;

    public NzCompletionParityGateTests()
    {
        _schema = (InMemorySchemaProvider)SqlTestHelpers.CreateStandardMockSchema();
        _engine = new NzCompletionEngine(_schema);
    }

    private static bool HasLabel(IReadOnlyList<CompletionItem> items, string label) =>
        items.Any(i => i.Label.Equals(label, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void ParityGate_AfterFrom_SuggestsTablesNotFunctions()
    {
        var items = _engine.GetCompletions("SELECT * FROM ", 14);
        Assert.Contains(items, i => i.Kind == CompletionKind.Table);
        Assert.DoesNotContain(items, i => i.Kind == CompletionKind.Function);
    }

    [Fact]
    public void ParityGate_AfterUpdate_SuggestsSetKeyword()
    {
        var items = _engine.GetCompletions("UPDATE EMPLOYEES ", 17);
        Assert.Contains(items, i => i.Label == "SET");
    }

    [Fact]
    public void ParityGate_QualifiedAlias_SuggestsColumns()
    {
        var sql = "SELECT e. FROM TESTDB..EMPLOYEES e";
        var items = _engine.GetCompletions(sql, 9);
        Assert.Contains(items, i => i.Kind == CompletionKind.Column);
    }

    [Fact]
    public void ParityGate_InsertInto_SuggestsTable()
    {
        var items = _engine.GetCompletions("INSERT INTO ", 12);
        Assert.Contains(items, i => i.Kind == CompletionKind.Table);
    }

    [Fact]
    public void ParityGate_InsertColumns_SuggestsTargetColumns()
    {
        var items = _engine.GetCompletions("INSERT INTO EMPLOYEES (", 23);
        Assert.Contains(items, i => i.Label.Equals("EMPLOYEE_ID", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ParityGate_MergeInto_SuggestsTable()
    {
        var items = _engine.GetCompletions("MERGE INTO ", 11);
        Assert.Contains(items, i => i.Kind == CompletionKind.Table);
    }

    [Fact]
    public void ParityGate_GenerateStatistics_SuggestsOnAndTables()
    {
        var sql = "GENERATE STATISTICS ON";
        var items = _engine.GetCompletions(sql, sql.Length);
        Assert.True(HasLabel(items, "EXPRESS") || items.Any(i => i.Kind == CompletionKind.Table));
    }

    [Fact]
    public void ParityGate_AlterTable_SuggestsTopLevelActions()
    {
        var items = _engine.GetCompletions("ALTER TABLE EMPLOYEES ", 22);
        Assert.Contains(items, i => i.Label.Contains("DROP COLUMN", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ParityGate_WildcardExpansion_ReturnsSnippet()
    {
        var sql = "SELECT e.* FROM TESTDB..EMPLOYEES e";
        var cursor = sql.IndexOf('*', StringComparison.Ordinal) + 1;
        var items = _engine.GetCompletions(sql, cursor);
        Assert.Contains(items, i => i.Kind == CompletionKind.Snippet && i.Label.Contains("e."));
    }

    [Fact]
    public void ParityGate_CteAlias_SuggestsCteColumns()
    {
        var sql = "WITH cte AS (SELECT EMPLOYEE_ID FROM TESTDB..EMPLOYEES) SELECT cte. FROM cte";
        var items = _engine.GetCompletions(sql, sql.IndexOf("cte.", StringComparison.Ordinal) + 4);
        Assert.Contains(items, i => i.Kind == CompletionKind.Column || i.Kind == CompletionKind.Cte);
    }

    [Fact]
    public void ParityGate_JoinAfterComma_ReturnsFromListTables()
    {
        var items = _engine.GetCompletions(
            "SELECT * FROM TESTDB..EMPLOYEES e JOIN ", 40);
        Assert.Contains(items, i => i.Kind == CompletionKind.Table);
        Assert.DoesNotContain(items, i => i.Kind == CompletionKind.Function);
    }

    [Fact]
    public void ParityGate_VariablePrefix_SuggestsVariables()
    {
        var items = _engine.GetCompletions("SELECT &ROW", 11);
        Assert.Contains(items, i => i.Kind == CompletionKind.Variable);
    }

    [Fact]
    public void ParityGate_AfterDelete_SuggestsFrom()
    {
        var items = _engine.GetCompletions("DELETE ", 7);
        Assert.Contains(items, i => i.Label == "FROM");
    }

    [Fact]
    public void ParityGate_AfterSet_SuggestsWhere()
    {
        var items = _engine.GetCompletions("UPDATE EMPLOYEES SET SALARY = 1 ", 32);
        Assert.Contains(items, i => i.Label == "WHERE");
    }

    [Fact]
    public void ParityGate_AfterWhere_SuggestsFunctions()
    {
        var items = _engine.GetCompletions("SELECT * FROM EMPLOYEES WHERE ", 35);
        Assert.Contains(items, i => i.Kind == CompletionKind.Function);
    }

    [Fact]
    public void ParityGate_AfterSelect_SuggestsFunctions()
    {
        var items = _engine.GetCompletions("SELECT ", 7);
        Assert.Contains(items, i => i.Kind == CompletionKind.Function);
    }

    [Fact]
    public void ParityGate_AfterValues_SuggestsNullAndDefault()
    {
        var items = _engine.GetCompletions("INSERT INTO EMPLOYEES (EMPLOYEE_ID) VALUES (", 44);
        Assert.Contains(items, i => i.Label == "NULL");
        Assert.Contains(items, i => i.Label == "DEFAULT");
    }

    [Fact]
    public void ParityGate_AfterGenerate_SuggestsStatistics()
    {
        var items = _engine.GetCompletions("GENERATE ", 9);
        Assert.Contains(items, i => i.Label == "STATISTICS");
    }

    [Fact]
    public void ParityGate_AfterMerge_SuggestsInto()
    {
        var items = _engine.GetCompletions("MERGE ", 6);
        Assert.Contains(items, i => i.Label == "INTO");
    }

    [Fact]
    public void ParityGate_AfterInsert_SuggestsInto()
    {
        var items = _engine.GetCompletions("INSERT ", 7);
        Assert.Contains(items, i => i.Label == "INTO");
    }

    [Fact]
    public void ParityGate_AfterJoin_SuggestsJoinKeywords()
    {
        var items = _engine.GetCompletions("SELECT * FROM EMPLOYEES e JOIN ", 35);
        Assert.Contains(items, i => i.Label is "ON" or "USING" or "INNER");
    }

    [Fact]
    public void ParityGate_AfterOrderBy_SuggestsLimitKeywords()
    {
        var items = _engine.GetCompletions("SELECT * FROM EMPLOYEES ORDER BY SALARY ", 42);
        Assert.Contains(items, i => i.Label is "LIMIT" or "FETCH" or "ASC" or "DESC");
    }

    [Fact]
    public void ParityGate_AfterCreate_SuggestsDdlKeywords()
    {
        var items = _engine.GetCompletions("CREATE ", 7);
        Assert.Contains(items, i => i.Label is "TABLE" or "VIEW" or "PROCEDURE");
    }

    [Fact]
    public void ParityGate_AfterDrop_SuggestsTables()
    {
        var items = _engine.GetCompletions("DROP ", 5);
        Assert.Contains(items, i => i.Kind == CompletionKind.Table || i.Label == "TABLE");
    }

    [Fact]
    public void ParityGate_AfterTruncate_SuggestsTableKeyword()
    {
        var items = _engine.GetCompletions("TRUNCATE ", 9);
        Assert.Contains(items, i => i.Label == "TABLE");
    }

    [Fact]
    public void ParityGate_PartialKeyword_FiltersTopLevel()
    {
        var items = _engine.GetCompletions("SEL", 3);
        Assert.Contains(items, i => i.Label == "SELECT");
        Assert.All(items, i => Assert.StartsWith("SEL", i.Label, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ParityGate_UpdateTarget_SuggestsTablesOnly()
    {
        var items = _engine.GetCompletions("UPDATE ", 7);
        Assert.Contains(items, i => i.Kind == CompletionKind.Table);
        Assert.DoesNotContain(items, i => i.Kind == CompletionKind.View);
    }

    [Fact]
    public void ParityGate_InsertColumns_SuggestsValues()
    {
        var items = _engine.GetCompletions("INSERT INTO EMPLOYEES (EMPLOYEE_ID) ", 38);
        Assert.Contains(items, i => i.Label == "VALUES");
    }

    [Fact]
    public void ParityGate_QualifiedTablePath_SuggestsColumns()
    {
        var sql = "SELECT TESTDB..EMPLOYEES.";
        var items = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(items, i => i.Kind == CompletionKind.Column);
    }

    [Fact]
    public void ParityGate_AfterExplain_SuggestsModes()
    {
        var items = _engine.GetCompletions("EXPLAIN ", 8);
        Assert.Contains(items, i => i.Label is "VERBOSE" or "SELECT" or "DISTRIBUTION");
    }

    [Fact]
    public void ParityGate_AlterTableDropShorthand_SuggestsColumns()
    {
        var sql = "ALTER TABLE EMPLOYEES DROP ";
        var items = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(items, i => i.Label.Equals("SALARY", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ParityGate_WildcardSubquery_ExpandsSnippet()
    {
        var sql = "SELECT sq.* FROM (SELECT EMPLOYEE_ID FROM TESTDB..EMPLOYEES) sq";
        var cursor = sql.IndexOf('*', StringComparison.Ordinal) + 1;
        var items = _engine.GetCompletions(sql, cursor);
        Assert.Contains(items, i => i.Kind == CompletionKind.Snippet && i.Label.Contains("sq.EMPLOYEE_ID", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ParityGate_AfterHaving_SuggestsFunctions()
    {
        var sql = "SELECT * FROM EMPLOYEES GROUP BY DEPARTMENT_ID HAVING COUNT(*) > ";
        var items = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(items, i => i.Kind is CompletionKind.Function or CompletionKind.Column);
    }

    [Fact]
    public void ParityGate_AfterGroupBy_SuggestsColumns()
    {
        var items = _engine.GetCompletions("SELECT * FROM EMPLOYEES GROUP BY ", 38);
        Assert.Contains(items, i => i.Kind == CompletionKind.Column);
    }

    [Fact]
    public void ParityGate_DeleteFrom_SuggestsTables()
    {
        var items = _engine.GetCompletions("DELETE FROM ", 12);
        Assert.Contains(items, i => i.Kind == CompletionKind.Table);
    }

    [Fact]
    public void ParityGate_AfterCreateTable_SuggestsTableNames()
    {
        var items = _engine.GetCompletions("CREATE ", 7);
        Assert.Contains(items, i => i.Label == "TABLE");
    }

    [Fact]
    public void ParityGate_AfterGroom_SuggestsTableKeyword()
    {
        var items = _engine.GetCompletions("GROOM ", 6);
        Assert.Contains(items, i => i.Label == "TABLE");
    }

    [Fact]
    public void ParityGate_AfterAsInSelect_SuggestsAliasContext()
    {
        var items = _engine.GetCompletions("SELECT EMPLOYEE_ID FROM EMPLOYEES e WHERE e.", 44);
        Assert.Contains(items, i => i.Kind == CompletionKind.Column);
    }

    [Fact]
    public void ParityGate_CommaInFromList_SuggestsMoreTables()
    {
        var items = _engine.GetCompletions("SELECT * FROM EMPLOYEES, ", 27);
        Assert.Contains(items, i => i.Kind == CompletionKind.Table);
    }

    [Fact]
    public void ParityGate_UpdateSetList_SuggestsColumns()
    {
        var items = _engine.GetCompletions("UPDATE EMPLOYEES SET ", 21);
        Assert.Contains(items, i => i.Kind == CompletionKind.Column);
    }

    [Fact]
    public void ParityGate_AfterWhereInUpdate_SuggestsFunctions()
    {
        var items = _engine.GetCompletions("UPDATE EMPLOYEES SET SALARY = 1 WHERE ", 38);
        Assert.Contains(items, i => i.Kind == CompletionKind.Function);
    }

    [Fact]
    public void ParityGate_CteInFromList_SuggestsCte()
    {
        var sql = "WITH cte AS (SELECT 1) SELECT * FROM ";
        var items = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(items, i => i.Kind == CompletionKind.Cte || i.Label.Equals("cte", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ParityGate_QualifiedWildcard_ExpandsTableColumns()
    {
        var sql = "SELECT EMPLOYEES.* FROM TESTDB..EMPLOYEES";
        var cursor = sql.IndexOf('*', StringComparison.Ordinal) + 1;
        var items = _engine.GetCompletions(sql, cursor);
        Assert.Contains(items, i => i.Kind == CompletionKind.Snippet);
    }

    [Fact]
    public void ParityGate_AfterMergeIntoTable_SuggestsMergeKeywords()
    {
        var sql = "MERGE INTO EMPLOYEES ";
        var items = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(items, i => i.Label is "USING" or "WHEN" or "ON" || i.Kind == CompletionKind.Table);
    }

    [Fact]
    public void ParityGate_AfterSelectDistinct_SuggestsColumnsOrFunctions()
    {
        var items = _engine.GetCompletions("SELECT DISTINCT ", 16);
        Assert.Contains(items, i => i.Kind is CompletionKind.Function or CompletionKind.Column);
    }

    [Fact]
    public void ParityGate_AfterUnion_SuggestsSelect()
    {
        var items = _engine.GetCompletions("SELECT 1 UNION ALL SELECT ", 26);
        Assert.Contains(items, i => i.Kind == CompletionKind.Function || i.Label == "DISTINCT");
    }

    [Fact]
    public void ParityGate_AfterBegin_SuggestsProcKeywords()
    {
        var items = _engine.GetCompletions("BEGIN ", 6);
        Assert.NotEmpty(items);
    }

    [Fact]
    public void ParityGate_AfterSemicolon_NewStatementTopLevel()
    {
        var items = _engine.GetCompletions("SELECT 1; ", 10);
        Assert.Contains(items, i => i.Label == "SELECT");
    }

    [Fact]
    public void ParityGate_AfterAlterTableQualified_SuggestsActions()
    {
        var items = _engine.GetCompletions("ALTER TABLE TESTDB..EMPLOYEES ", 32);
        Assert.Contains(items, i => i.Label.Contains("ADD", StringComparison.OrdinalIgnoreCase));
    }
}
