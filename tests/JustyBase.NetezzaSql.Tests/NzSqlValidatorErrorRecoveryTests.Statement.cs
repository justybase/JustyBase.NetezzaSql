using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed partial class NzSqlValidatorErrorRecoveryTests
{
    [Fact]
    public void Validate_ErrorRecovery_CaseWithMultipleWhenButNoEnd()
    {
        SqlTestHelpers.ExpectSyntaxError(
            @"SELECT CASE
    WHEN SALARY > 5000 THEN 'High'
    WHEN SALARY > 3000 THEN 'Medium'
    ELSE 'Low'
FROM TESTDB..EMPLOYEES;",
            _schema);
    }


    [Fact]
    public void Validate_ErrorRecovery_InWithTrailingComma()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "SELECT * FROM TESTDB..EMPLOYEES WHERE EMPLOYEE_ID IN (1, 2,);",
            _schema);
    }


    [Fact]
    public void Validate_SelectError_MissingSemicolonIsTolerated()
    {
        var result = SqlTestHelpers.Validate("SELECT 1");
        Assert.DoesNotContain(result.Errors, e => e.Code.StartsWith("PAR"));
        Assert.DoesNotContain(result.Errors, e => e.Code.StartsWith("LEX"));
    }


    [Fact]
    public void Validate_JoinError_ValidCallStatementTopLevel()
    {
        SqlTestHelpers.ExpectValid("CALL SOME_PROC_NAME()");
    }


    [Fact]
    public void Validate_JoinError_ValidCallStatementSchemaQualified()
    {
        SqlTestHelpers.ExpectValid("CALL JUST_DATA.ADMIN.SOME_PROC_NAME()");
    }


    [Fact]
    public void Validate_JoinError_ValidCallStatementWithArguments()
    {
        SqlTestHelpers.ExpectValid("CALL SOME_PROC_NAME('test', 123, 45.67)");
    }


    [Fact]
    public void Validate_SelectError2_OrderByWithTrailingComma()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT id FROM t ORDER BY id,");
    }


    [Fact]
    public void Compare_WithJsParser_SemicolonAfterFrom()
    {
        var result = SqlTestHelpers.Validate("SELECT * FROM ;", _schema);
        // JS: PAR001 - "Missing table or subquery after FROM."
        // C#: should be PAR001
        Assert.Contains(result.Errors, e => e.Code == "PAR001");
    }


    [Fact]
    public void Compare_WithJsParser_MultiStmtWithTypo()
    {
        var result = SqlTestHelpers.Validate("SELECT * FROM t WEHRE 1=1; SELECT 1", _schema);
        // JS: PAR001 - "Redundant input, expecting EOF but found: 1"
        // C#: WEHRE consumed as alias, then 1=1 unexpected, ; stops statement, SELECT 1 never reached
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Code.StartsWith("PAR"));
    }
}
