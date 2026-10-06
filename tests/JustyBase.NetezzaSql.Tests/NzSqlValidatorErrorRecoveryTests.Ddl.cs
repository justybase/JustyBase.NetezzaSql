using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed partial class NzSqlValidatorErrorRecoveryTests
{
    [Fact]
    public void Validate_SelectError_MissingJoinKeywordBetweenTablesWithOn()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "SELECT * FROM TESTDB..EMPLOYEES E TESTDB..DEPARTMENTS D ON E.DEPARTMENT_ID = D.DEPARTMENT_ID;",
            _schema);
    }


    [Fact]
    public void Validate_SelectError_MissingJoinedTableAfterJoinKeyword()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "SELECT * FROM TESTDB..EMPLOYEES E JOIN ON E.DEPARTMENT_ID = 1;",
            _schema);
    }


    [Fact]
    public void Validate_JoinError_ValidExecuteProcedureAlternative()
    {
        SqlTestHelpers.ExpectValid("EXECUTE PROCEDURE SOME_PROC_NAME()");
    }


    [Fact]
    public void Validate_JoinError_ValidExecuteAlternative()
    {
        SqlTestHelpers.ExpectValid("EXECUTE SOME_PROC_NAME()");
    }


    [Fact]
    public void Validate_JoinError_ValidExecProcedureShorthand()
    {
        SqlTestHelpers.ExpectValid("EXEC PROCEDURE SOME_PROC_NAME()");
    }


    [Fact]
    public void Validate_JoinError_RejectJoinWithMissingTable()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT * FROM t1 INNER JOIN ON t1.id = 1");
    }


    [Fact]
    public void Validate_JoinError_DuplicateTableAliasInSameFromClause()
    {
        SqlTestHelpers.ExpectErrorCode(
            @"SELECT X.* FROM TESTDB..EMPLOYEES X
JOIN TESTDB..EMPLOYEES X ON X.EMPLOYEE_ID = X.EMPLOYEE_ID",
            "SQL011",
            _schema);
    }


    [Fact]
    public void Validate_JoinError_DuplicateTableNameWithoutAlias()
    {
        SqlTestHelpers.ExpectErrorCode(
            "SELECT * FROM TESTDB..EMPLOYEES JOIN TESTDB..EMPLOYEES ON 1=1",
            "SQL011",
            _schema);
    }

    // ========================================================================
    // DDL - syntax errors (extended)
    // ========================================================================

    [Fact]
    public void Validate_DdlError_RejectCreateTableMissingColumnType()
    {
        SqlTestHelpers.ExpectSyntaxError("CREATE TABLE t (col1)");
    }


    [Fact]
    public void Validate_SelectError2_UnquotedReservedKeywordAsTableName()
    {
        SqlTestHelpers.ExpectErrorCode("SELECT * FROM FROM", "PAR003");
    }


    [Fact]
    public void Validate_ErrorRecovery_KeywordAsTableName_Detected()
    {
        // WHERE keyword used as table name after FROM
        var result = SqlTestHelpers.Validate(
            "SELECT * FROM WHERE; SELECT 1",
            SqlTestHelpers.CreateStandardMockSchema());
        var errors = result.Errors.Where(e => e.Code.StartsWith("PAR")).ToList();
        Assert.Contains(errors, e => e.Code == "PAR001");
    }


    [Fact]
    public void Compare_WithJsParser_KeywordAsTableFrom()
    {
        var result = SqlTestHelpers.Validate("SELECT * FROM WHERE; SELECT 1", _schema);
        // JS: SQL015 (reserved keyword as table name) - C# doesn't have SQL015
        Assert.NotEmpty(result.Errors); // both emit errors
        Assert.Contains(result.Errors, e => e.Code.StartsWith("PAR"));
    }


    [Fact]
    public void Compare_WithJsParser_MissingTableAfterFrom()
    {
        var result = SqlTestHelpers.Validate("SELECT * FROM", _schema);
        // JS: PAR001 - "Missing table or subquery after FROM."
        // C#: PAR001 with similar message
        Assert.Contains(result.Errors, e => e.Code == "PAR001");
    }
}
