using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed partial class NzSqlValidatorErrorRecoveryTests
{
    [Fact]
    public void Validate_ErrorRecovery_UnclosedDoubleQuotedIdentifier()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT \"unclosed_id FROM t;");
    }


    [Fact]
    public void Validate_ErrorRecovery_UnclosedStringInWhereClause()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "SELECT * FROM TESTDB..EMPLOYEES WHERE FIRST_NAME = 'John;",
            _schema);
    }


    [Fact]
    public void Validate_ErrorRecovery_UnclosedStringInInsertValues()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "INSERT INTO TESTDB..EMPLOYEES (FIRST_NAME) VALUES ('John);",
            _schema);
    }

    // ========================================================================
    // Error Recovery - CASE without END
    // ========================================================================

    [Fact]
    public void Validate_ErrorRecovery_CaseWithoutEndInSelect()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT CASE WHEN 1 = 1 THEN 'yes';");
    }


    [Fact]
    public void Validate_ErrorRecovery_UnclosedParenthesisInNestedExpression()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT ((a + b) * c FROM t;");
    }


    [Fact]
    public void Validate_SelectError_InvalidJoinTypeKeywordSequence()
    {
        SqlTestHelpers.ExpectSyntaxError(
            @"SELECT * FROM TESTDB..EMPLOYEES E LEFT RIGHT JOIN TESTDB..DEPARTMENTS D ON E.DEPARTMENT_ID = D.DEPARTMENT_ID;",
            _schema);
    }


    [Fact]
    public void Validate_JoinError_RejectLeftRightJoinInvalidCombo()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT * FROM t1 LEFT RIGHT JOIN t2 ON t1.id = t2.id");
    }


    [Fact]
    public void Validate_DdlError_RejectCreateTableUnclosedParenthesis()
    {
        SqlTestHelpers.ExpectSyntaxError("CREATE TABLE t (id INT, name VARCHAR(100)");
    }


    [Fact]
    public void Validate_ErrorRecovery_UnclosedParen_PAR107_Detected()
    {
        var result = SqlTestHelpers.Validate("SELECT (1 + 2 FROM t");
        Assert.Contains(result.Errors, e => e.Code == "PAR107");
    }


    [Fact]
    public void Validate_ErrorRecovery_UnclosedString_PAR110_Detected()
    {
        var result = SqlTestHelpers.Validate("SELECT 'hello");
        Assert.Contains(result.Errors, e => e.Code == "PAR110");
    }


    [Fact]
    public void Validate_ErrorRecovery_UnclosedComment_PAR111_Detected()
    {
        var result = SqlTestHelpers.Validate("SELECT 1 /* unclosed");
        Assert.Contains(result.Errors, e => e.Code == "PAR111");
    }


    [Fact]
    public void Compare_WithJsParser_UnclosedParenInExpr()
    {
        var result = SqlTestHelpers.Validate("SELECT (1 + 2 FROM t", _schema);
        // JS: PAR001 - "Expecting token of type RParen but found FROM"
        // C#: PAR107 (more specific)
        Assert.Contains(result.Errors, e => e.Code is "PAR107" or "PAR001" or "PAR112");
    }


    [Fact]
    public void Compare_WithJsParser_UnclosedParenAtEof()
    {
        var result = SqlTestHelpers.Validate("SELECT (1 + 2", _schema);
        // JS: PAR001 - "Missing closing ')' before 'end of statement'"
        // C#: PAR001 (parser error) or PAR112 (structural)
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Code.StartsWith("PAR"));
    }


    [Fact]
    public void Compare_WithJsParser_UnclosedString()
    {
        var result = SqlTestHelpers.Validate("SELECT 'hello", _schema);
        // JS: LEX001 - "Lexer error: unexpected character: ' at offset: 7"
        // C#: PAR110 (structural scanner - improved!)
        Assert.Contains(result.Errors, e => e.Code is "PAR110" or "LEX001");
    }


    [Fact]
    public void Compare_WithJsParser_UnclosedComment()
    {
        var result = SqlTestHelpers.Validate("SELECT 1 /* unclosed", _schema);
        // JS: PAR001 - "Parser error: ..." (no specific unclosed-comment detection)
        // C#: PAR111 (structural scanner - improved!)
        Assert.Contains(result.Errors, e => e.Code is "PAR111" or "PAR001" or "LEX001");
    }
}
