using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class NzSqlValidatorSemanticTests
{
    private readonly ISchemaProvider _schema;

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
    public void Validate_Semantic_NonExistentColumnInUpdateSet()
    {
        SqlTestHelpers.ExpectErrorCode(
            "UPDATE TESTDB..EMPLOYEES SET FAKE_COLUMN = 'x';",
            "SQL004",
            _schema);
    }

    [Fact]
    public void Validate_Semantic_NonExistentColumnInDeleteWhere()
    {
        SqlTestHelpers.ExpectErrorCode(
            "DELETE FROM TESTDB..EMPLOYEES WHERE FAKE_COLUMN = 1;",
            "SQL004",
            _schema);
    }

    [Fact]
    public void Validate_Semantic_InvalidDbTableForm()
    {
        var result = SqlTestHelpers.Validate("SELECT 1 FROM TESTDB.EMPLOYEES;", _schema);
        Assert.Contains(result.Errors, e => e.Code == "SQL007");
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
    public void Validate_Boolean_NonBooleanExpressionInOnClause()
    {
        SqlTestHelpers.ExpectErrorCode(
            @"SELECT * FROM TESTDB..EMPLOYEES E JOIN TESTDB..DEPARTMENTS D ON E.DEPARTMENT_ID + D.DEPARTMENT_ID;",
            "SQL010",
            _schema);
    }

    // ========================================================================
    // Function validation
    // ========================================================================

    [Fact]
    public void Validate_Function_KnownAggregateFunctions()
    {
        SqlTestHelpers.ExpectValid(
            "SELECT SUM(SALARY), AVG(SALARY), MIN(SALARY), MAX(SALARY), COUNT(*) FROM TESTDB..EMPLOYEES;",
            _schema);
    }

    [Fact]
    public void Validate_Function_KnownStringFunctions()
    {
        SqlTestHelpers.ExpectValid(
            "SELECT UPPER(FIRST_NAME), LOWER(LAST_NAME), LENGTH(FIRST_NAME), TRIM(FIRST_NAME), SUBSTR(FIRST_NAME, 1, 3) FROM TESTDB..EMPLOYEES;",
            _schema);
    }

    [Fact]
    public void Validate_Function_KnownConditionalFunctions()
    {
        SqlTestHelpers.ExpectValid(
            "SELECT COALESCE(MANAGER_ID, 0), NVL(MANAGER_ID, 0), NULLIF(SALARY, 0), DECODE(STATUS, 'A', 1, 0) FROM TESTDB..EMPLOYEES;",
            _schema);
    }

    [Fact]
    public void Validate_Function_KnownNumericFunctions()
    {
        SqlTestHelpers.ExpectValid("SELECT ABS(-1), CEIL(1.5), FLOOR(1.5), ROUND(1.234, 2), MOD(10, 3), POWER(2, 3), SQRT(16);");
    }

    [Fact]
    public void Validate_Function_SqlExtensionsDatePartFunctions()
    {
        SqlTestHelpers.ExpectValid(
            "SELECT YEAR(HIRE_DATE), MONTH(HIRE_DATE), DAY(HIRE_DATE) FROM TESTDB..EMPLOYEES;",
            _schema);
    }

    [Fact]
    public void Validate_Function_SqlExtensionsDateTimeUtilityFunctions()
    {
        SqlTestHelpers.ExpectValid(
            "SELECT DAYS_BETWEEN(NOW(), NOW()), HOURS_BETWEEN(NOW(), NOW()), MINUTES_BETWEEN(NOW(), NOW()), SECONDS_BETWEEN(NOW(), NOW()), WEEKS_BETWEEN(NOW(), NOW()) FROM TESTDB..EMPLOYEES;",
            _schema);
    }

    [Fact]
    public void Validate_Function_NextWeekNextMonthNextQuarterNextYear()
    {
        SqlTestHelpers.ExpectValid(
            "SELECT NEXT_WEEK(HIRE_DATE), NEXT_MONTH(HIRE_DATE), NEXT_QUARTER(HIRE_DATE), NEXT_YEAR(HIRE_DATE), THIS_WEEK(HIRE_DATE), THIS_MONTH(HIRE_DATE), THIS_QUARTER(HIRE_DATE), THIS_YEAR(HIRE_DATE) FROM TESTDB..EMPLOYEES;",
            _schema);
    }

    public static TheoryData<string, string> NetezzaExtensionFunctionData => new()
    {
        { "BTRIM", "SELECT BTRIM('  hi  ');" },
        { "INSTR", "SELECT INSTR('Hello World', 'o');" },
        { "STRPOS", "SELECT STRPOS('Hello World', 'o');" },
        { "UNICHR", "SELECT UNICHR(65);" },
        { "UNICODE", "SELECT UNICODE('A');" },
        { "UNICODES", "SELECT UNICODES('AZ');" },
        { "OVERLAPS", "SELECT OVERLAPS(1, 2, 3, 4);" },
        { "DURATION_ADD", "SELECT DURATION_ADD(1, 2);" },
        { "DURATION_SUBTRACT", "SELECT DURATION_SUBTRACT(2, 1);" },
        { "TIMEOFDAY", "SELECT TIMEOFDAY();" },
        { "TIMEZONE", "SELECT TIMEZONE(NOW(), 'UTC', 'UTC');" },
        { "HEX_TO_BINARY", "SELECT HEX_TO_BINARY('DEADBEEF');" },
        { "HEX_TO_GEOMETRY", "SELECT HEX_TO_GEOMETRY('00');" },
        { "INT_TO_STRING", "SELECT INT_TO_STRING(42, 16);" },
        { "STRING_TO_INT", "SELECT STRING_TO_INT('2A', 16);" },
        { "ISFALSE", "SELECT ISFALSE(1 = 0);" },
        { "ISNOTFALSE", "SELECT ISNOTFALSE(1 = 1);" },
        { "ISTRUE", "SELECT ISTRUE(1 = 1);" },
        { "ISNOTTRUE", "SELECT ISNOTTRUE(1 = 0);" },
        { "VERSION", "SELECT VERSION();" },
        { "GET_VIEWDEF", "SELECT GET_VIEWDEF('EMP_VIEW');" },
        { "SETSEED", "SELECT SETSEED(0.5);" },
        { "DCEIL", "SELECT DCEIL(42.8);" },
        { "DFLOOR", "SELECT DFLOOR(42.8);" },
        { "FPOW", "SELECT FPOW(9.0, 3.0);" },
        { "NUMERIC_SQRT", "SELECT NUMERIC_SQRT(2);" },
        { "POW", "SELECT POW(9.0, 3.0);" },
        { "INT1AND", "SELECT INT1AND(3, 6);" },
        { "INT1OR", "SELECT INT1OR(3, 6);" },
        { "INT1XOR", "SELECT INT1XOR(3, 6);" },
        { "INT1NOT", "SELECT INT1NOT(3);" },
        { "INT1SHL", "SELECT INT1SHL(3, 1, 6);" },
        { "INT1SHR", "SELECT INT1SHR(3, 1, 6);" },
        { "INT2AND", "SELECT INT2AND(3, 6);" },
        { "INT2OR", "SELECT INT2OR(3, 6);" },
        { "INT2XOR", "SELECT INT2XOR(3, 6);" },
        { "INT2NOT", "SELECT INT2NOT(3);" },
        { "INT2SHL", "SELECT INT2SHL(3, 1, 6);" },
        { "INT2SHR", "SELECT INT2SHR(3, 1, 6);" },
        { "INT4AND", "SELECT INT4AND(3, 6);" },
        { "INT4OR", "SELECT INT4OR(3, 6);" },
        { "INT4XOR", "SELECT INT4XOR(3, 6);" },
        { "INT4NOT", "SELECT INT4NOT(3);" },
        { "INT4SHL", "SELECT INT4SHL(3, 1, 6);" },
        { "INT4SHR", "SELECT INT4SHR(3, 1, 6);" },
        { "INT8AND", "SELECT INT8AND(3, 6);" },
        { "INT8OR", "SELECT INT8OR(3, 6);" },
        { "INT8XOR", "SELECT INT8XOR(3, 6);" },
        { "INT8NOT", "SELECT INT8NOT(3);" },
        { "INT8SHL", "SELECT INT8SHL(3, 1, 6);" },
        { "INT8SHR", "SELECT INT8SHR(3, 1, 6);" },
    };

    [Theory]
    [MemberData(nameof(NetezzaExtensionFunctionData))]
    public void Validate_Function_NetezzaExtensionFunction(string functionName, string sql)
    {
        Assert.False(string.IsNullOrWhiteSpace(functionName));
        SqlTestHelpers.ExpectValid(sql);
    }

    [Fact]
    public void Validate_Function_UnknownFunctionNameTypo()
    {
        SqlTestHelpers.ExpectErrorCode("SELECT SUMM(SALARY) FROM TESTDB..EMPLOYEES;", "SQL011", _schema);
    }

    [Fact]
    public void Validate_Function_UnknownFunctionRandomName()
    {
        SqlTestHelpers.ExpectErrorCode("SELECT TOTALLY_FAKE_FUNC(1, 2, 3);", "SQL011");
    }

    [Fact]
    public void Validate_Function_CountStar()
    {
        SqlTestHelpers.ExpectValid("SELECT COUNT(*) FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_Function_CountDistinctColumn()
    {
        SqlTestHelpers.ExpectValid(
            "SELECT COUNT(DISTINCT DEPARTMENT_ID) FROM TESTDB..EMPLOYEES;",
            _schema);
    }

    // ========================================================================
    // Semantic validation - additional patterns
    // ========================================================================

    [Fact]
    public void Validate_SemanticAdditional_ColumnNotInTable()
    {
        SqlTestHelpers.ExpectErrorCode(
            "SELECT NONEXISTENT_COL FROM TESTDB..EMPLOYEES;",
            "SQL004",
            _schema);
    }

    [Fact]
    public void Validate_SemanticAdditional_TableNotInDatabase()
    {
        SqlTestHelpers.ExpectErrorCode("SELECT * FROM TESTDB..NONEXISTENT_TABLE;", "SQL006", _schema);
    }

    [Fact]
    public void Validate_DoubleDotExistingTable_NoError()
    {
        var schema = SqlTestHelpers.CreateMockSchemaProvider([
            new("DIMACCOUNT", "ADMIN", "JUST_DATA", ["ID", "NAME"])
        ]);
        SqlTestHelpers.ExpectValid("SELECT * FROM JUST_DATA..DIMACCOUNT;", schema);
        SqlTestHelpers.ExpectValid("SELECT ID FROM JUST_DATA..DIMACCOUNT;", schema);
    }

    [Fact]
    public void Validate_DoubleDotAfterDelayedSchemaSync_NoError()
    {
        // Reproduce the bug: if .. lookup fails BEFORE table is loaded (absent cached),
        // then AddTable must clear the absent cache for DB..TABLE form too.
        var provider = new InMemorySchemaProvider();

        // Add some other table so HasTables() returns true
        provider.AddTable(new TableInfo("OTHER", "PUBLIC", "TESTDB",
            Columns: new[] { new ColumnInfo("X") }));

        // First lookup: DIMACCOUNT not yet loaded → absent cached for JUST_DATA..DIMACCOUNT
        Assert.Null(provider.GetTable("JUST_DATA", null, "DIMACCOUNT"));

        // Now the table is loaded via schema sync
        provider.AddTable(new TableInfo("DIMACCOUNT", "ADMIN", "JUST_DATA",
            Columns: new[] { new ColumnInfo("ID"), new ColumnInfo("NAME") }));

        // After AddTable, .. lookup should find it
        var result = provider.GetTable("JUST_DATA", null, "DIMACCOUNT");
        Assert.NotNull(result);
        Assert.Equal("DIMACCOUNT", result.Name);

        Assert.True(provider.TableExists("JUST_DATA", null, "DIMACCOUNT"));
        Assert.True(provider.TableExists("JUST_DATA", "ADMIN", "DIMACCOUNT"));
        SqlTestHelpers.ExpectValid("SELECT * FROM JUST_DATA..DIMACCOUNT;", provider);
        SqlTestHelpers.ExpectValid("SELECT * FROM JUST_DATA.ADMIN.DIMACCOUNT;", provider);
    }

    [Fact]
    public void Validate_SemanticAdditional_InvalidDatabaseTableFormWithSchema()
    {
        SqlTestHelpers.ExpectErrorCode("SELECT * FROM TESTDB.EMPLOYEES;", "SQL007", _schema);
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
    public void Validate_SemanticAdditional_NonBooleanExpressionInWhere()
    {
        SqlTestHelpers.ExpectErrorCode(
            "SELECT * FROM TESTDB..EMPLOYEES E WHERE E.SALARY + 1;",
            "SQL010",
            _schema);
    }

    [Fact]
    public void Validate_SemanticAdditional_UnknownFunctionName()
    {
        SqlTestHelpers.ExpectErrorCode("SELECT TOTALLY_FAKE_FUNCTION(1) AS X;", "SQL011");
    }

    [Fact]
    public void Validate_SemanticAdditional_VarcharWithoutLengthWarning()
    {
        SqlTestHelpers.ExpectWarningCode("SELECT 1::VARCHAR;", "SQL012");
    }

    [Fact]
    public void Validate_SemanticAdditional_InvalidDataTypeInCreateTable()
    {
        SqlTestHelpers.ExpectErrorCode(
            "CREATE TABLE TESTDB..BAD_TYPE (ID FAKE_TYPE);",
            "SQL013",
            _schema);
    }

    [Fact]
    public void Validate_SemanticAdditional_ExcessTypeParameters()
    {
        SqlTestHelpers.ExpectErrorCode(
            "CREATE TABLE TESTDB..BAD_PARAMS (ID INT4(10,2));",
            "SQL014",
            _schema);
    }

    [Fact]
    public void Validate_SemanticAdditional_KnownAggregateFunctions()
    {
        SqlTestHelpers.ExpectValid(
            @"SELECT COUNT(*) AS C, SUM(SALARY) AS S, AVG(SALARY) AS A, MIN(SALARY) AS MN, MAX(SALARY) AS MX FROM TESTDB..EMPLOYEES;",
            _schema);
    }

    [Fact]
    public void Validate_SemanticAdditional_KnownStringFunctions()
    {
        SqlTestHelpers.ExpectValid(
            "SELECT UPPER('hello') AS U, LOWER('HELLO') AS L, TRIM('  hi  ') AS T, LENGTH('abc') AS LN;");
    }

    [Fact]
    public void Validate_SemanticAdditional_KnownNumericFunctions()
    {
        SqlTestHelpers.ExpectValid(
            "SELECT ABS(-5) AS A, CEIL(3.2) AS C, FLOOR(3.8) AS F, ROUND(3.456, 2) AS R, MOD(10, 3) AS M;");
    }

    [Fact]
    public void Validate_SemanticAdditional_KnownDateFunctions()
    {
        SqlTestHelpers.ExpectValid("SELECT DATE_PART('year', CURRENT_DATE) AS Y, NOW() AS N;");
    }

    [Fact]
    public void Validate_SemanticAdditional_NonExistentColumnInWhereClause()
    {
        SqlTestHelpers.ExpectErrorCode(
            "SELECT * FROM TESTDB..EMPLOYEES WHERE FAKE_COLUMN = 1;",
            "SQL004",
            _schema);
    }

    [Fact]
    public void Validate_SemanticAdditional_NonExistentColumnInOrderBy()
    {
        SqlTestHelpers.ExpectErrorCode(
            "SELECT * FROM TESTDB..EMPLOYEES ORDER BY FAKE_COLUMN;",
            "SQL004",
            _schema);
    }

    [Fact]
    public void Validate_SemanticAdditional_NonExistentColumnInGroupBy()
    {
        SqlTestHelpers.ExpectErrorCode(
            "SELECT FAKE_COLUMN, COUNT(*) FROM TESTDB..EMPLOYEES GROUP BY FAKE_COLUMN;",
            "SQL004",
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
    public void Validate_JoinColumnExistence_FakeColumnInLeftTableOn()
    {
        SqlTestHelpers.ExpectErrorCode(
            @"SELECT * FROM TESTDB..EMPLOYEES E
JOIN TESTDB..DEPARTMENTS D ON E.FAKE_COLUMN = D.DEPARTMENT_ID;",
            "SQL004",
            _schema);
    }

    [Fact]
    public void Validate_JoinColumnExistence_FakeColumnInRightTableOn()
    {
        SqlTestHelpers.ExpectErrorCode(
            @"SELECT * FROM TESTDB..EMPLOYEES E
JOIN TESTDB..DEPARTMENTS D ON E.DEPARTMENT_ID = D.FAKE_COLUMN;",
            "SQL004",
            _schema);
    }

    [Fact]
    public void Validate_JoinColumnExistence_ColumnsFromJoinedSubqueries()
    {
        SqlTestHelpers.ExpectValid(
            @"SELECT A.ID, B.NAME
FROM (SELECT EMPLOYEE_ID AS ID FROM TESTDB..EMPLOYEES) A
JOIN (SELECT DEPARTMENT_ID AS ID, DEPARTMENT_NAME AS NAME FROM TESTDB..DEPARTMENTS) B
ON A.ID = B.ID;",
            _schema);
    }

    [Fact]
    public void Validate_JoinColumnExistence_NonExistentColumnFromJoinedSubquery()
    {
        SqlTestHelpers.ExpectErrorCode(
            @"SELECT A.ID, B.FAKE_NAME
FROM (SELECT EMPLOYEE_ID AS ID FROM TESTDB..EMPLOYEES) A
JOIN (SELECT DEPARTMENT_ID AS ID FROM TESTDB..DEPARTMENTS) B
ON A.ID = B.ID;",
            "SQL004",
            _schema);
    }

    // ========================================================================
    // ADVANCED: Object existence (table/CTE/alias/subquery)
    // ========================================================================

    [Fact]
    public void Validate_ObjectExistence_CteDefinedEarlierInSameQuery()
    {
        SqlTestHelpers.ExpectValid(@"WITH MY_CTE AS (SELECT 1 AS COL)
SELECT * FROM MY_CTE;");
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
    public void Validate_ComplexError_NonExistentColumnInAnalyticsPartitionBy()
    {
        SqlTestHelpers.ExpectErrorCode(
            @"SELECT 
    ROW_NUMBER() OVER (PARTITION BY FAKE_COLUMN ORDER BY EMPLOYEE_ID) AS RN
FROM TESTDB..EMPLOYEES;",
            "SQL004",
            _schema);
    }

    [Fact]
    public void Validate_ComplexError_NonExistentColumnInAnalyticsOrderBy()
    {
        SqlTestHelpers.ExpectErrorCode(
            @"SELECT 
    ROW_NUMBER() OVER (PARTITION BY DEPARTMENT_ID ORDER BY FAKE_COLUMN) AS RN
FROM TESTDB..EMPLOYEES;",
            "SQL004",
            _schema);
    }

    [Fact]
    public void Validate_ComplexError_NonExistentColumnInGroupByRollup()
    {
        SqlTestHelpers.ExpectErrorCode(
            "SELECT COUNT(*) FROM TESTDB..EMPLOYEES GROUP BY ROLLUP(FAKE_COLUMN);",
            "SQL004",
            _schema);
    }

    [Fact]
    public void Validate_ComplexError_NonExistentColumnInCaseWhen()
    {
        SqlTestHelpers.ExpectErrorCode(
            @"SELECT 
    CASE WHEN FAKE_COLUMN > 100 THEN 'High' ELSE 'Low' END
FROM TESTDB..EMPLOYEES;",
            "SQL004",
            _schema);
    }

    // ========================================================================
    // SQL004 enhanced message: table name + position
    // ========================================================================

    [Fact]
    public void SQL004_MessageContainsTableName_Qualified()
    {
        var result = SqlTestHelpers.Validate(
            "SELECT E.NO_SUCH_COLUMN FROM TESTDB..EMPLOYEES E;",
            _schema);

        var diag = Assert.Single(result.Errors, d => d.Code == "SQL004");
        Assert.Contains("EMPLOYEES", diag.Message);
        Assert.Contains("NO_SUCH_COLUMN", diag.Message);
        Assert.StartsWith("SQL004:", diag.Message);
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
    public void SQL004_HasCorrectPosition()
    {
        var result = SqlTestHelpers.Validate(
            "SELECT FAKE_COLUMN FROM TESTDB..EMPLOYEES;",
            _schema);

        var diag = Assert.Single(result.Errors, d => d.Code == "SQL004");
        // "FAKE_COLUMN" is at column 8 (1-based) in "SELECT FAKE_COLUMN..."
        Assert.True(diag.Position.Line > 0, "StartLine should be positive");
        Assert.True(diag.Position.Column > 0, "StartColumn should be positive");
        Assert.True(diag.EndLine > 0, "EndLine should be set");
        Assert.Equal(diag.Position.Column + "FAKE_COLUMN".Length, diag.EndColumn);
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
