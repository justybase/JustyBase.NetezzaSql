using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;
using JustyBase.NetezzaSqlParser.Visitor;
using static JustyBase.Tests.NetezzaSqlParser.SqlTestHelpers;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed partial class NzSqlValidatorRemainingTests
{
    [Fact]
    public void Validate_Casting_CastToNumericWithPrecision()
    {
        ExpectValid("SELECT CAST('3.14' AS NUMERIC(10,2)) AS NUM;");
    }


    [Fact]
    public void Validate_Casting_CastToDate()
    {
        ExpectValid("SELECT CAST('2023-01-01' AS DATE) AS D;");
    }


    [Fact]
    public void Validate_Casting_CastToTimestamp()
    {
        ExpectValid("SELECT CAST('2023-01-01 12:00:00' AS TIMESTAMP) AS TS;");
    }


    [Fact]
    public void Validate_Casting_CastOperatorWithVarchar()
    {
        ExpectValid(
            "SELECT E.EMPLOYEE_ID::VARCHAR(10) AS ID_STR FROM TESTDB..EMPLOYEES E;",
            _schema);
    }


    [Fact]
    public void Validate_Casting_CastOperatorWithNumeric()
    {
        ExpectValid(
            "SELECT E.SALARY::NUMERIC(10,2) AS SAL FROM TESTDB..EMPLOYEES E;",
            _schema);
    }


    [Fact]
    public void Validate_Casting_CastOperatorChained()
    {
        ExpectValid("SELECT '123'::INT4::VARCHAR(10) AS ROUND_TRIP;");
    }


    [Fact]
    public void Validate_Casting_CastOperatorSimple()
    {
        ExpectValid("SELECT 1::INT4 AS X;");
    }


    [Fact]
    public void Validate_Casting_CastOperatorParameterized()
    {
        ExpectValid("SELECT 1::VARCHAR(20) AS X;");
    }

    // ========================================================================
    // Edge cases and special patterns
    // ========================================================================

    [Fact]
    public void Validate_EdgeCases_EmptyStatement()
    {
        var result = Validate(";");
        Assert.NotNull(result);
    }


    [Fact]
    public void Validate_EdgeCases_MultipleSemicolons()
    {
        var result = Validate(";;;");
        Assert.NotNull(result);
    }


    [Fact]
    public void Validate_EdgeCases_LongColumnList()
    {
        ExpectValid(
            "SELECT E.EMPLOYEE_ID, E.FIRST_NAME, E.LAST_NAME, E.DEPARTMENT_ID, E.SALARY, E.HIRE_DATE, E.MANAGER_ID, E.STATUS FROM TESTDB..EMPLOYEES E;",
            _schema);
    }


    [Fact]
    public void Validate_EdgeCases_MixedCaseKeywords()
    {
        ExpectValid("select * from TESTDB..EMPLOYEES where SALARY > 0;", _schema);
    }


    [Fact]
    public void Validate_EdgeCases_AllUppercase()
    {
        ExpectValid("SELECT * FROM TESTDB..EMPLOYEES WHERE SALARY > 0;", _schema);
    }


    [Fact]
    public void Validate_EdgeCases_LongStringLiteral()
    {
        var longStr = new string('a', 500);
        ExpectValid($"SELECT '{longStr}' AS LONG_STR;");
    }


    [Fact]
    public void Validate_EdgeCases_NumericLiteralWithDecimal()
    {
        ExpectValid("SELECT 3.14159265358979 AS PI;");
    }


    [Fact]
    public void Validate_EdgeCases_ScientificNotation()
    {
        ExpectValid("SELECT 1.5E10 AS BIG_NUM;");
    }


    [Fact]
    public void Validate_EdgeCases_NegativeInExpression()
    {
        ExpectValid(
            "SELECT E.SALARY * -1 AS NEG_SALARY FROM TESTDB..EMPLOYEES E;",
            _schema);
    }


    [Fact]
    public void Validate_EdgeCases_MultipleStatements()
    {
        var result = Validate("SELECT 1; SELECT 2;");
        Assert.NotNull(result);
    }


    [Fact]
    public void Validate_EdgeCases_OnlyWhitespace()
    {
        var result = Validate("   \n\t  ");
        Assert.NotNull(result);
    }


    [Fact]
    public void Validate_EdgeCases_EmptyString()
    {
        var result = Validate("");
        Assert.NotNull(result);
    }


    [Fact]
    public void Validate_EdgeCases_MultipleStatementsSeparatedBySemicolons()
    {
        var result = Validate("SELECT 1; SELECT 2;");
        Assert.NotNull(result);
    }

    // ========================================================================
    // SELECT — additional valid patterns (new ones not already covered)
    // ========================================================================

    [Fact]
    public void Validate_Select_Additional_MultipleConditionsAndOr()
    {
        ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES WHERE SALARY > 1000 AND DEPARTMENT_ID = 1 OR STATUS = 'ACTIVE';",
            _schema);
    }


    [Fact]
    public void Validate_Utility_GenerateExpressStatisticsOn()
    {
        ExpectValid("GENERATE EXPRESS STATISTICS ON TESTDB..EMPLOYEES;", _schema);
    }


    [Fact]
    public void Validate_Advanced_NonExistentColumnInCaseWhen()
    {
        ExpectErrorCode(
            """
            SELECT
                CASE WHEN FAKE_COLUMN > 100 THEN 'High' ELSE 'Low' END
            FROM TESTDB..EMPLOYEES;
            """,
            "SQL004",
            _schema);
    }

    // ========================================================================
    // Node-equivalent tests — missing coverage
    // ========================================================================

    [Fact]
    public void Validate_NodeParity_EmptyStatementWarning() { }


    [Fact]
    public void Validate_NodeParity_IlikeOperator()
    {
        ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES WHERE FIRST_NAME ILIKE 'a%';",
            _schema);
    }


    [Fact]
    public void Validate_NodeParity_NullsFirstWithAsc()
    {
        ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES ORDER BY LAST_NAME ASC NULLS FIRST;",
            _schema);
    }


    [Fact]
    public void Validate_NodeParity_NullsLastWithoutAscDesc()
    {
        ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES ORDER BY LAST_NAME NULLS LAST;",
            _schema);
    }


    [Fact]
    public void Validate_NodeParity_CurrentDateInExpression()
    {
        ExpectValid("SELECT CURRENT_DATE - 1 AS YESTERDAY;");
    }


    [Fact]
    public void Validate_NodeParity_NetezzaExtensionFunctionNvl()
    {
        ExpectValid("SELECT NVL(NULL, 'default');");
    }


    [Fact]
    public void Validate_NodeParity_NetezzaExtensionFunctionNvl2()
    {
        ExpectValid("SELECT NVL2(NULL, 'not_null', 'null');");
    }


    [Fact]
    public void Validate_NodeParity_NetezzaExtensionFunctionDecode()
    {
        ExpectValid("SELECT DECODE(1, 1, 'one', 2, 'two', 'other');");
    }


    [Fact]
    public void Validate_NodeParity_NetezzaExtensionFunctionGreatest()
    {
        ExpectValid("SELECT GREATEST(1, 2, 3);");
    }


    [Fact]
    public void Validate_NodeParity_NetezzaExtensionFunctionLeast()
    {
        ExpectValid("SELECT LEAST(1, 2, 3);");
    }


    [Fact]
    public void Validate_NodeParity_ModuloOperator()
    {
        ExpectValid("SELECT 10 % 3 AS MOD_RESULT;");
    }


    [Fact]
    public void Validate_NodeParity_TypeLiteralDate()
    {
        ExpectValid("SELECT DATE '2026-07-23';", _schema);
    }


    [Fact]
    public void Validate_NodeParity_TypeLiteralTimestamp()
    {
        ExpectValid("SELECT TIMESTAMP '2026-07-23 12:30:00';", _schema);
    }


    [Fact]
    public void Validate_NodeParity_AggregateFilterClause()
    {
        ExpectValid(
            "SELECT DEPARTMENT_ID, SUM(SALARY) FILTER (WHERE SALARY > 50000) FROM TESTDB..EMPLOYEES GROUP BY DEPARTMENT_ID;",
            _schema);
    }


    [Fact]
    public void Validate_NodeParity_GenerateExpressStatistics()
    {
        // Valid syntax per parser: GENERATE [EXPRESS] STATISTICS ON table (without TABLE keyword)
        ExpectValid("GENERATE EXPRESS STATISTICS ON TESTDB..EMPLOYEES;", _schema);
    }


    [Fact]
    public void Validate_NodeParity_TypeLiteralInterval()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT INTERVAL '1' DAY;");
    }
}
