using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed partial class NzSqlValidatorSemanticTests
{
    public NzSqlValidatorSemanticTests()
    {
        _schema = SqlTestHelpers.CreateStandardMockSchema();
    }

    // ========================================================================
    // Semantic validation - column/table errors
    // ========================================================================

    [Fact]
    public void Validate_Semantic_NonExistentTableQualified()
    {
        SqlTestHelpers.ExpectErrorCode(
            "SELECT * FROM TESTDB.PUBLIC.NONEXISTENT_TABLE;",
            "SQL006",
            _schema);
    }


    [Fact]
    public void Validate_Semantic_NonExistentColumnQualified()
    {
        SqlTestHelpers.ExpectErrorCode(
            "SELECT E.FAKE_COLUMN FROM TESTDB..EMPLOYEES E;",
            "SQL004",
            _schema);
    }


    [Fact]
    public void Validate_Semantic_AmbiguousUnqualifiedColumnAcrossJoins()
    {
        SqlTestHelpers.ExpectErrorCode(
            @"SELECT DEPARTMENT_ID FROM TESTDB..EMPLOYEES E JOIN TESTDB..DEPARTMENTS D ON E.DEPARTMENT_ID = D.DEPARTMENT_ID;",
            "SQL008",
            _schema);
    }

    // ========================================================================
    // Boolean expression validation
    // ========================================================================

    [Fact]
    public void Validate_Boolean_NonBooleanExpressionInWhere()
    {
        SqlTestHelpers.ExpectErrorCode(
            "SELECT * FROM TESTDB..EMPLOYEES WHERE SALARY + 1;",
            "SQL010",
            _schema);
    }


    [Fact]
    public void Validate_SemanticAdditional_AmbiguousColumnWithoutQualifier()
    {
        SqlTestHelpers.ExpectErrorCode(
            @"SELECT DEPARTMENT_ID FROM TESTDB..EMPLOYEES E JOIN TESTDB..DEPARTMENTS D ON E.DEPARTMENT_ID = D.DEPARTMENT_ID;",
            "SQL008",
            _schema);
    }


    [Fact]
    public void Validate_SemanticAdditional_QualifiedColumnResolvesAmbiguity()
    {
        SqlTestHelpers.ExpectValid(
            @"SELECT E.DEPARTMENT_ID FROM TESTDB..EMPLOYEES E JOIN TESTDB..DEPARTMENTS D ON E.DEPARTMENT_ID = D.DEPARTMENT_ID;",
            _schema);
    }


    [Fact]
    public void Validate_SemanticAdditional_UnqualifiedColumnInSubqueryInnerScopeShadowsOuter()
    {
        SqlTestHelpers.ExpectValid(
            @"SELECT 1 FROM TESTDB..EMPLOYEES E WHERE E.DEPARTMENT_ID = (SELECT MAX(DEPARTMENT_ID) FROM TESTDB..DEPARTMENTS)",
            _schema);
    }


    [Fact]
    public void Validate_SemanticAdditional_UnqualifiedColumnInUnionAllBranchScopeIsolates()
    {
        SqlTestHelpers.ExpectValid(
            @"SELECT 1 FROM TESTDB..EMPLOYEES E1 WHERE DEPARTMENT_ID = 5 UNION ALL SELECT 1 FROM TESTDB..DEPARTMENTS D WHERE DEPARTMENT_ID = 5",
            _schema);
    }


    [Fact]
    public void Validate_SemanticAdditional_UnqualifiedColumnInUnionAllSameTableDifferentAliases()
    {
        SqlTestHelpers.ExpectValid(
            @"SELECT 1 FROM TESTDB..EMPLOYEES E1 WHERE DEPARTMENT_ID = 5 UNION ALL SELECT 1 FROM TESTDB..EMPLOYEES E2 WHERE DEPARTMENT_ID = 5",
            _schema);
    }


    [Fact]
    public void Validate_SemanticAdditional_UnqualifiedColumnInUnionAllWithoutAliases()
    {
        SqlTestHelpers.ExpectValid(
            @"SELECT 1 FROM TESTDB..EMPLOYEES WHERE DEPARTMENT_ID = 5 UNION ALL SELECT 1 FROM TESTDB..EMPLOYEES WHERE DEPARTMENT_ID = 5",
            _schema);
    }


    [Fact]
    public void Validate_SemanticAdditional_OriginalNameUsedWhenCteColumnListRenames()
    {
        SqlTestHelpers.ExpectErrorCode(
            @"WITH DEPT_KEYS (KEY_ID) AS (
    SELECT D.DEPARTMENT_ID FROM TESTDB..DEPARTMENTS D
)
SELECT DK.DEPARTMENT_ID FROM DEPT_KEYS DK;",
            "SQL004",
            _schema);
    }

    // ========================================================================
    // ADVANCED: Column existence in JOINs
    // ========================================================================

    [Fact]
    public void Validate_JoinColumnExistence_ColumnInOnExistsInBothTables()
    {
        SqlTestHelpers.ExpectValid(
            @"SELECT * FROM TESTDB..EMPLOYEES E
JOIN TESTDB..DEPARTMENTS D ON E.DEPARTMENT_ID = D.DEPARTMENT_ID;",
            _schema);
    }


    [Fact]
    public void Validate_ObjectExistence_NonExistentTableGracefully()
    {
        var result = SqlTestHelpers.Validate(@"WITH REAL_CTE AS (SELECT 1 AS COL)
SELECT * FROM FAKE_CTE;");
        Assert.NotNull(result);
    }


    [Fact]
    public void Validate_ObjectExistence_TempTableCreatedEarlierInScript()
    {
        SqlTestHelpers.ExpectValid(
            @"CREATE TEMP TABLE MY_TEMP (ID INT4, NAME VARCHAR(50));
SELECT ID, NAME FROM MY_TEMP;");
    }


    [Fact]
    public void Validate_ObjectExistence_CtasTableSubsequentStatement()
    {
        SqlTestHelpers.ExpectValid(
            @"CREATE TABLE CTAS_RESULT AS (SELECT 1 AS A, 2 AS B);
SELECT A, B FROM CTAS_RESULT;");
    }


    [Fact]
    public void Validate_ObjectExistence_TableAliasOutsideScope()
    {
        SqlTestHelpers.ExpectErrorCode(
            @"SELECT OUTER_ALIAS.FAKE_COL FROM (
    SELECT E.EMPLOYEE_ID FROM TESTDB..EMPLOYEES E
) OUTER_ALIAS;",
            "SQL004",
            _schema);
    }


    [Fact]
    public void Validate_ObjectExistence_NonExistentColumnInSubqueryAlias()
    {
        SqlTestHelpers.ExpectErrorCode(
            @"SELECT OUTER_SUB.FAKE_COL FROM (
    SELECT E.EMPLOYEE_ID FROM TESTDB..EMPLOYEES E
) OUTER_SUB;",
            "SQL004",
            _schema);
    }


    [Fact]
    public void Validate_ObjectExistence_TableCreatedViaDdlIsReferenceable()
    {
        SqlTestHelpers.ExpectValid(
            @"CREATE TABLE NEW_DDL_TABLE (ID INT4 CONSTRAINT PK_NEW PRIMARY KEY, NAME VARCHAR(100));
SELECT ID, NAME FROM NEW_DDL_TABLE;");
    }


    [Fact]
    public void Validate_ObjectExistence_ViewCreatedViaCreateViewIsReferenceable()
    {
        SqlTestHelpers.ExpectValid(
            @"CREATE VIEW TEST_VIEW AS SELECT EMPLOYEE_ID, FIRST_NAME FROM TESTDB..EMPLOYEES;
SELECT EMPLOYEE_ID, FIRST_NAME FROM TEST_VIEW;");
    }

    // ========================================================================
    // ADVANCED: Error detection in complex SQL
    // ========================================================================

    [Fact]
    public void Validate_ComplexError_MissingColumnInDeeplyNestedSubquery()
    {
        SqlTestHelpers.ExpectErrorCode(
            @"SELECT * FROM (
    SELECT * FROM (
        SELECT NONEXISTENT_COL FROM TESTDB..EMPLOYEES
    ) L2
) L1;",
            "SQL004",
            _schema);
    }


    [Fact]
    public void Validate_ComplexError_AmbiguousColumnAcrossCtes()
    {
        SqlTestHelpers.ExpectErrorCode(
            @"WITH 
    CTE1 AS (SELECT EMPLOYEE_ID, DEPARTMENT_ID FROM TESTDB..EMPLOYEES),
    CTE2 AS (SELECT EMPLOYEE_ID, DEPARTMENT_ID FROM TESTDB..EMPLOYEES)
SELECT EMPLOYEE_ID, DEPARTMENT_ID
FROM CTE1 C1
JOIN CTE2 C2 ON 1=1;",
            "SQL008",
            _schema);
    }


    [Fact]
    public void Validate_ComplexError_NonExistentTableInSubqueryWithinCteGracefully()
    {
        var result = SqlTestHelpers.Validate(@"WITH MY_CTE AS (
    SELECT * FROM NONEXISTENT_TABLE
)
SELECT * FROM MY_CTE;");
        Assert.NotNull(result);
    }


    [Fact]
    public void SQL004_MessageContainsTableName_Unqualified()
    {
        var result = SqlTestHelpers.Validate(
            "SELECT NO_SUCH_COLUMN FROM TESTDB..EMPLOYEES;",
            _schema);

        var diag = Assert.Single(result.Errors, d => d.Code == "SQL004");
        Assert.Contains("EMPLOYEES", diag.Message);
        Assert.Contains("NO_SUCH_COLUMN", diag.Message);
        Assert.StartsWith("SQL004:", diag.Message);
    }


    [Fact]
    public void CteWithUnqualifiedStar_ExpandsColumns()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("DIMDATE", "ADMIN", "JUST_DATA", Columns: new[]
        {
            new ColumnInfo("DATEKEY"), new ColumnInfo("CALENDARQUARTER")
        }));

        SqlTestHelpers.ExpectValid(
            @"WITH CTE1 AS (
    SELECT * FROM JUST_DATA..DIMDATE
)
SELECT DATEKEY FROM CTE1 WHERE CALENDARQUARTER > 0", schema);
    }


    [Fact]
    public void CteWithSelectStarQualified_ReportsErrorOnNonExistentColumn()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("DIMDATE", "ADMIN", "JUST_DATA", Columns: new[]
        {
            new ColumnInfo("DATEKEY"), new ColumnInfo("CALENDARQUARTER")
        }));

        SqlTestHelpers.ExpectErrorCode(
            @"WITH CTE1 AS (
    SELECT D.* FROM JUST_DATA..DIMDATE D
)
SELECT C.NONEXISTENT FROM CTE1 C",
            "SQL004",
            schema);
    }


    [Fact]
    public void CteWithJoinAndMultipleQualifiedStars_ExpandsColumns()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("EMPLOYEES", "PUBLIC", "HR", Columns: new[]
        {
            new ColumnInfo("EMP_ID"), new ColumnInfo("EMP_NAME"), new ColumnInfo("DEPT_FK")
        }));
        schema.AddTable(new TableInfo("DEPARTMENTS", "PUBLIC", "HR", Columns: new[]
        {
            new ColumnInfo("DEPT_ID"), new ColumnInfo("DEPT_NAME")
        }));

        SqlTestHelpers.ExpectValid(
            @"WITH CTE_JOIN AS (
    SELECT E.*, D.*
    FROM HR..EMPLOYEES E
    JOIN HR..DEPARTMENTS D ON E.DEPT_FK = D.DEPT_ID
)
SELECT CJ.EMP_ID, CJ.DEPT_NAME FROM CTE_JOIN CJ
WHERE CJ.EMP_ID > 0", schema);
    }


    [Fact]
    public void CteWithJoinAndMultipleQualifiedStars_ReportsErrorOnNonExistentColumn()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("EMPLOYEES", "PUBLIC", "HR", Columns: new[]
        {
            new ColumnInfo("EMP_ID"), new ColumnInfo("EMP_NAME"), new ColumnInfo("DEPT_FK")
        }));
        schema.AddTable(new TableInfo("DEPARTMENTS", "PUBLIC", "HR", Columns: new[]
        {
            new ColumnInfo("DEPT_ID"), new ColumnInfo("DEPT_NAME")
        }));

        SqlTestHelpers.ExpectErrorCode(
            @"WITH CTE_JOIN AS (
    SELECT E.*, D.*
    FROM HR..EMPLOYEES E
    JOIN HR..DEPARTMENTS D ON E.DEPT_FK = D.DEPT_ID
)
SELECT CJ.NONEXISTENT FROM CTE_JOIN CJ",
            "SQL004",
            schema);
    }

    // ========================================================================
    // Subquery with SELECT * wildcard (InferColumnsFromSelect)
    // ========================================================================

    [Fact]
    public void SubqueryWithUnqualifiedStar_SkipsColumnInference()
    {
        // When a subquery in FROM/JOIN uses SELECT *, InferColumnsFromSelect
        // should skip it (no columns inferred) rather than adding a bogus "*" column.
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("DIMDATE", "ADMIN", "JUST_DATA", Columns: new[]
        {
            new ColumnInfo("DATEKEY"), new ColumnInfo("CALENDARQUARTER")
        }));

        // The subquery has no alias for its columns, so we can't reference them
        // by name - but it should not crash and should not produce bogus errors.
        var result = SqlTestHelpers.Validate(
            @"SELECT * FROM (SELECT * FROM JUST_DATA..DIMDATE) SQ", schema);
        Assert.Empty(result.Errors);
    }


    [Fact]
    public void SubqueryExplicitColumn_ResolvesCorrectly()
    {
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("DIMDATE", "ADMIN", "JUST_DATA", Columns: new[]
        {
            new ColumnInfo("DATEKEY"), new ColumnInfo("CALENDARQUARTER")
        }));

        SqlTestHelpers.ExpectValid(
            @"SELECT SQ.DATEKEY FROM (SELECT D.DATEKEY FROM JUST_DATA..DIMDATE D) SQ", schema);
    }


    [Fact]
    public void SubqueryWithQualifiedStar_GracefulDegradation()
    {
        // When a subquery uses D.*, InferColumnsFromSelect (static, no schema)
        // skips it rather than adding a bogus "*" column. The subquery alias
        // has no columns — should not crash, should not produce false errors.
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("DIMDATE", "ADMIN", "JUST_DATA", Columns: new[]
        {
            new ColumnInfo("DATEKEY"), new ColumnInfo("CALENDARQUARTER")
        }));

        var result = SqlTestHelpers.Validate(
            @"SELECT * FROM (SELECT D.* FROM JUST_DATA..DIMDATE D) SQ", schema);
        Assert.Empty(result.Errors);
    }


    [Fact]
    public void SQL004_CTE_SameColumnBothCTE_AtLeastOneErrorWithPosition()
    {
        var sql = @"
WITH 
    CTE1 AS (SELECT NO_SUCH_COLUMN FROM TESTDB..EMPLOYEES),
    CTE2 AS (SELECT NO_SUCH_COLUMN FROM TESTDB..EMPLOYEES)
SELECT c1.NO_SUCH_COLUMN, c2.NO_SUCH_COLUMN
FROM CTE1 C1
JOIN CTE2 C2 ON c1.NO_SUCH_COLUMN = c2.NO_SUCH_COLUMN
";

        var result = SqlTestHelpers.Validate(sql, _schema);

        var sql004 = result.Errors.Where(d => d.Code == "SQL004").ToList();

        // Minimum: at least one error (Node parity)
        Assert.True(sql004.Count >= 1, $"Expected >= 1 SQL004 diagnostic, got {sql004.Count}");

        // First error must have table name and correct position
        var first = sql004.First();
        Assert.Contains("NO_SUCH_COLUMN", first.Message);
        Assert.True(first.Message.Contains("not found in table"),
            "Message should name the table");
        Assert.True(first.Position.Line > 0);
        Assert.True(first.Position.Column > 0);
        Assert.True(first.EndLine > 0, "EndLine should be set");
        Assert.Equal(first.Position.Column + "NO_SUCH_COLUMN".Length, first.EndColumn);
    }
}
