using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed partial class NzSqlValidatorErrorRecoveryTests
{
    [Fact]
    public void Validate_ErrorRecovery_NestedCaseWithoutInnerEnd()
    {
        SqlTestHelpers.ExpectSyntaxError(
            @"SELECT CASE
    WHEN 1 = 1 THEN CASE WHEN 2 = 2 THEN 'nested'
    ELSE 'outer'
END;");
    }


    [Fact]
    public void Validate_ErrorRecovery_CaseWithoutEndInProcedure()
    {
        SqlTestHelpers.ExpectSyntaxError(
            @"CREATE OR REPLACE PROCEDURE BAD_CASE_PROC()
RETURNS INT4
LANGUAGE NZPLSQL AS
BEGIN_PROC
BEGIN
    DECLARE v_result VARCHAR(20);
    v_result := CASE WHEN 1 = 1 THEN 'yes';
    RETURN 1;
END;
END_PROC;");
    }

    // ========================================================================
    // Error Recovery - parenthesis mismatch
    // ========================================================================

    [Fact]
    public void Validate_ErrorRecovery_UnclosedParenthesisInFunctionCall()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "SELECT UPPER(name FROM TESTDB..EMPLOYEES;",
            _schema);
    }


    [Fact]
    public void Validate_ErrorRecovery_ExtraClosingParenthesis()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "SELECT UPPER(name)) FROM TESTDB..EMPLOYEES;",
            _schema);
    }


    [Fact]
    public void Validate_ErrorRecovery_MismatchedParenthesesInInClause()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT * FROM t WHERE id IN (1, 2, 3;");
    }

    // ========================================================================
    // Error Recovery - IN () edge cases
    // ========================================================================

    [Fact]
    public void Validate_ErrorRecovery_EmptyInClause()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "SELECT * FROM TESTDB..EMPLOYEES WHERE EMPLOYEE_ID IN ();",
            _schema);
    }


    [Fact]
    public void Validate_ErrorRecovery_InWithoutClosingParenthesis()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "SELECT * FROM TESTDB..EMPLOYEES WHERE EMPLOYEE_ID IN (1, 2;",
            _schema);
    }

    // ========================================================================
    // SELECT - additional syntax errors
    // ========================================================================

    [Fact]
    public void Validate_SelectError_MissingTableNameAfterFrom()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT * FROM;");
    }


    [Fact]
    public void Validate_SelectError_OrderByWithoutExpression()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT * FROM TESTDB..EMPLOYEES ORDER BY;", _schema);
    }


    [Fact]
    public void Validate_SelectError_IncompleteBetweenExpressionMissingAnd()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "SELECT * FROM TESTDB..EMPLOYEES WHERE SALARY BETWEEN 1000;",
            _schema);
    }


    [Fact]
    public void Validate_SelectError_IncompleteCaseWithoutEnd()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT CASE WHEN 1 = 1 THEN 'yes';");
    }


    [Fact]
    public void Validate_DdlError_RejectCreateTableDuplicateComma()
    {
        SqlTestHelpers.ExpectSyntaxError("CREATE TABLE t (id INT,, name VARCHAR(100))");
    }


    [Fact]
    public void Validate_SelectError2_HavingWithoutExpression()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT COUNT(*) FROM t GROUP BY id HAVING");
    }


    [Fact]
    public void Validate_WindowError_RejectOverWithStrayComma()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "SELECT SUM(x) OVER (PARTITION BY a, ORDER BY b) FROM t");
    }

    // ========================================================================
    // New error recovery features: PAR004 keyword typo, PAR107 unclosed paren,
    // PAR108 CASE without END, PAR110 unclosed string, PAR111 unclosed comment
    // ========================================================================

    [Fact]
    public void Validate_ErrorRecovery_KeywordTypo_PAR004_SuggestsSelect()
    {
        var result = SqlTestHelpers.Validate("SELEC 1");
        Assert.Contains(result.Errors, e =>
            e.Code == "PAR004" && e.SuggestedFix == "SELECT");
    }


    [Fact]
    public void Validate_ErrorRecovery_CaseWithoutEnd_PAR108_Detected()
    {
        var result = SqlTestHelpers.Validate("SELECT CASE WHEN 1=1 THEN 2");
        Assert.Contains(result.Errors, e => e.Code == "PAR108");
    }


    [Fact]
    public void Validate_ErrorRecovery_CaseWithEnd_No_PAR108()
    {
        var result = SqlTestHelpers.Validate("SELECT CASE WHEN 1=1 THEN 2 END FROM t");
        Assert.DoesNotContain(result.Errors, e => e.Code == "PAR108");
    }


    [Fact]
    public void Validate_ErrorRecovery_MismatchedParens_PAR112_Detected()
    {
        var result = SqlTestHelpers.Validate("SELECT (1 + 2");
        Assert.Contains(result.Errors, e => e.Code == "PAR112");
    }


    [Fact]
    public void Compare_WithJsParser_CaseWithoutEnd()
    {
        var result = SqlTestHelpers.Validate("SELECT CASE WHEN 1=1 THEN 2", _schema);
        // JS: PAR005 - "CASE expression must end with END."
        // C#: PAR108 (improved, more specific than PAR005 + location)
        Assert.Contains(result.Errors, e => e.Code is "PAR108" or "PAR005");
    }


    [Fact]
    public void Compare_WithJsParser_CaseWithoutEndFrom()
    {
        var result = SqlTestHelpers.Validate("SELECT CASE WHEN 1=1 THEN 2 FROM t", _schema);
        // JS: PAR005 - "CASE expression must end with END."
        // C#: PAR108 (improved)
        Assert.Contains(result.Errors, e => e.Code is "PAR108" or "PAR005");
    }


    [Fact]
    public void Compare_WithJsParser_DoubleComma()
    {
        var result = SqlTestHelpers.Validate("SELECT 1,,2", _schema);
        // JS: PAR002 - "Consecutive commas (,,) indicate a missing expression..."
        Assert.Contains(result.Errors, e => e.Code == "PAR002");
    }
}
