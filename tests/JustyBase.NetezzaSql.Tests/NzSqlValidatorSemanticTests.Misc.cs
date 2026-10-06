using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed partial class NzSqlValidatorSemanticTests
{
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

    // ========================================================================
    // CTE with SELECT * wildcard expansion
    // ========================================================================

    [Fact]
    public void CteWithSelectStarQualified_ExpandsColumns()
    {
        // Reproduce the bug: CTE with D.* (qualified star) was parsed as
        // ColumnReference(Name="*", Qualifier="D") instead of StarExpression,
        // so BuildCteColumns never expanded the wildcard into actual column names.
        var schema = new InMemorySchemaProvider();
        schema.AddTable(new TableInfo("DIMDATE", "ADMIN", "JUST_DATA", Columns: new[]
        {
            new ColumnInfo("DATEKEY"), new ColumnInfo("CALENDARQUARTER")
        }));

        SqlTestHelpers.ExpectValid(
            @"WITH CTE1 AS (
    SELECT D.* FROM JUST_DATA..DIMDATE D
)
SELECT C.DATEKEY FROM CTE1 C
WHERE C.CALENDARQUARTER > 0", schema);
    }
}
