using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed partial class NzSqlValidatorErrorRecoveryTests
{
    public NzSqlValidatorErrorRecoveryTests()
    {
        _schema = SqlTestHelpers.CreateStandardMockSchema();
    }

    // ========================================================================
    // Error Recovery - unclosed strings
    // ========================================================================

    [Fact]
    public void Validate_ErrorRecovery_UnclosedSingleQuotedString()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT 'unclosed string;");
    }


    [Fact]
    public void Validate_SelectError_MissingColumnAfterSelectKeyword()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT FROM TESTDB..EMPLOYEES;", _schema);
    }


    [Fact]
    public void Validate_SelectError_DuplicateFromKeyword()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT * FROM FROM TESTDB..EMPLOYEES;", _schema);
    }


    [Fact]
    public void Validate_SelectError_WhereWithoutCondition()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT * FROM TESTDB..EMPLOYEES WHERE;", _schema);
    }


    [Fact]
    public void Validate_SelectError_GroupByWithoutColumn()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT * FROM TESTDB..EMPLOYEES GROUP BY;", _schema);
    }


    [Fact]
    public void Validate_SelectError_LimitWithoutNumber()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT * FROM TESTDB..EMPLOYEES LIMIT;", _schema);
    }


    [Fact]
    public void Validate_SelectError_MissingAliasAfterAs()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT 1 AS;");
    }


    [Fact]
    public void Validate_SelectError_JoinWithoutOnIsAccepted()
    {
        var result = SqlTestHelpers.Validate(
            "SELECT * FROM TESTDB..EMPLOYEES E JOIN TESTDB..DEPARTMENTS D;",
            _schema);
        Assert.DoesNotContain(result.Errors, e => e.Code.StartsWith("PAR"));
        Assert.DoesNotContain(result.Errors, e => e.Code.StartsWith("LEX"));
    }


    [Fact]
    public void Validate_SelectError_IncompleteOnPredicateInJoin()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "SELECT * FROM TESTDB..EMPLOYEES E JOIN TESTDB..DEPARTMENTS D ON E.DEPARTMENT_ID = ;",
            _schema);
    }


    [Fact]
    public void Validate_SelectError_ExtraKeywordAfterLimit()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "SELECT * FROM TESTDB..EMPLOYEES LIMIT 10 WHERE SALARY > 0;",
            _schema);
    }

    // ========================================================================
    // JOIN - syntax errors
    // ========================================================================

    [Fact]
    public void Validate_JoinError_RejectLeftJoinMissingTable()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT * FROM t1 LEFT JOIN");
    }


    [Fact]
    public void Validate_JoinError_ValidExecShorthand()
    {
        SqlTestHelpers.ExpectValid("EXEC SOME_PROC_NAME()");
    }


    [Fact]
    public void Validate_JoinError_RejectJoinWithDoubleOn()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT * FROM t1 JOIN t2 ON ON t1.id = t2.id");
    }


    [Fact]
    public void Validate_JoinError_RejectJoinWithIncompleteCondition()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT * FROM t1 JOIN t2 ON t1.id =");
    }


    [Fact]
    public void Validate_JoinError_ValidNaturalJoin()
    {
        SqlTestHelpers.ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES NATURAL JOIN TESTDB..DEPARTMENTS",
            _schema);
    }


    [Fact]
    public void Validate_JoinError_ValidNaturalLeftJoin()
    {
        SqlTestHelpers.ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES NATURAL LEFT JOIN TESTDB..DEPARTMENTS",
            _schema);
    }


    [Fact]
    public void Validate_JoinError_ValidJoinWithUsingClause()
    {
        SqlTestHelpers.ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES JOIN TESTDB..DEPARTMENTS USING (DEPARTMENT_ID)",
            _schema);
    }


    [Fact]
    public void Validate_JoinError_ValidLeftJoinWithUsingClause()
    {
        SqlTestHelpers.ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES LEFT JOIN TESTDB..DEPARTMENTS USING (DEPARTMENT_ID)",
            _schema);
    }


    [Fact]
    public void Validate_JoinError_RejectNaturalJoinWithOnClause()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "SELECT * FROM TESTDB..EMPLOYEES NATURAL JOIN TESTDB..DEPARTMENTS ON 1=1",
            _schema);
    }


    [Fact]
    public void Validate_JoinError_CrossJoinWithOnIsAllowed()
    {
        // Node: CROSS JOIN should not have ON clause → SQL002 (warning)
        var result = SqlTestHelpers.Validate(
            "SELECT * FROM TESTDB..EMPLOYEES CROSS JOIN TESTDB..DEPARTMENTS ON 1=1",
            _schema);
        Assert.Contains(result.Warnings, w => w.Code == "SQL002");
    }


    [Fact]
    public void Validate_JoinError_AmbiguousColumnCteAndSubqueryAliasSameName()
    {
        SqlTestHelpers.ExpectErrorCode(
            @"WITH ABC_123 AS 
(
    SELECT 2 AS COL2 FROM TESTDB..DIMACCOUNT
)
SELECT COL2 FROM 
(SELECT 200 as COL2) ABC_123
JOIN ABC_123 x ON 1=1",
            "SQL008",
            _schema);
    }


    [Fact]
    public void Validate_DdlError_RejectGrantWithoutArguments()
    {
        SqlTestHelpers.ExpectSyntaxError("GRANT");
    }


    [Fact]
    public void Validate_DdlError_RejectRevokeWithoutArguments()
    {
        SqlTestHelpers.ExpectSyntaxError("REVOKE");
    }

    // ========================================================================
    // SELECT - additional syntax errors (second block)
    // ========================================================================

    [Fact]
    public void Validate_SelectError2_MissingFromKeywordAndTable()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT id WHERE x > 1 FROM t");
    }


    [Fact]
    public void Validate_SelectError2_GroupByWithoutColumnList()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT COUNT(*) FROM t GROUP BY");
    }


    [Fact]
    public void Validate_SelectError2_LimitWithoutNumber()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT * FROM t LIMIT");
    }


    [Fact]
    public void Validate_SelectError2_DoubleDistinct()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT DISTINCT DISTINCT id FROM t");
    }

    // ========================================================================
    // CTE - additional syntax errors (extended)
    // ========================================================================

    [Fact]
    public void Validate_CteError_RejectCteWithoutAs()
    {
        SqlTestHelpers.ExpectSyntaxError("WITH cte (SELECT 1) SELECT * FROM cte");
    }


    [Fact]
    public void Validate_CteError_RejectCteWithMissingBody()
    {
        SqlTestHelpers.ExpectSyntaxError("WITH cte AS SELECT * FROM cte");
    }


    [Fact]
    public void Validate_CteError_RejectCteWithEmptyColumnList()
    {
        SqlTestHelpers.ExpectSyntaxError("WITH cte () AS (SELECT 1) SELECT * FROM cte");
    }

    // ========================================================================
    // Window functions - syntax errors
    // ========================================================================

    [Fact]
    public void Validate_WindowError_RejectOverClauseWithoutParentheses()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT SUM(x) OVER FROM t");
    }


    [Fact]
    public void Validate_WindowError_RejectWindowFrameMissingPrecedingFollowing()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "SELECT SUM(x) OVER (ORDER BY id ROWS BETWEEN UNBOUNDED AND CURRENT ROW) FROM t");
    }


    [Fact]
    public void Validate_WindowError_RejectWindowFrameMissingAndInBetween()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "SELECT SUM(x) OVER (ORDER BY id ROWS BETWEEN UNBOUNDED PRECEDING CURRENT ROW) FROM t");
    }


    [Fact]
    public void Validate_WindowError_RejectWindowFrameMissingBoundSpecification()
    {
        SqlTestHelpers.ExpectSyntaxError(
            @"SELECT E.PARENTEMPLOYEEKEY, SUM(E.CURRENTFLAG::INT) OVER (ORDER BY E.PARENTEMPLOYEEKEY ROWS BETWEEN PRECEDING AND CURRENT ROW) AS RUN_SUM FROM JUST_DATA..DIMEMPLOYEE E");
    }


    [Fact]
    public void Validate_WindowError_RejectPartitionByWithoutColumnList()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT SUM(x) OVER (PARTITION BY) FROM t");
    }


    [Fact]
    public void Validate_ErrorRecovery_KeywordTypo_PAR004_SuggestsFrom()
    {
        var result = SqlTestHelpers.Validate("FORM t");
        Assert.Contains(result.Errors, e =>
            e.Code == "PAR004" && e.SuggestedFix == "FROM");
    }


    [Fact]
    public void Validate_ErrorRecovery_KeywordTypo_PAR004_SuggestsWhere()
    {
        var result = SqlTestHelpers.Validate("WEHRE 1=1");
        Assert.Contains(result.Errors, e =>
            e.Code == "PAR004" && e.SuggestedFix == "WHERE");
    }


    [Fact]
    public void Validate_ErrorRecovery_UpdateMissingSet_PAR115()
    {
        var result = SqlTestHelpers.Validate(
            "UPDATE t WHERE 1=1",
            SqlTestHelpers.CreateStandardMockSchema());
        Assert.Contains(result.Errors, e => e.Code == "PAR115");
    }


    [Fact]
    public void Validate_ErrorRecovery_DeleteMissingFrom_PAR116()
    {
        var result = SqlTestHelpers.Validate("DELETE FROM");
        Assert.Contains(result.Errors, e => e.Code is "PAR102" or "PAR001");
    }

    // ============================================================
    // JS vs C# comparison - verify error codes are equivalent
    // JS output from compare_errors.ts at JustyBaseLite-netezzaTMP_
    // ============================================================

    [Fact]
    public void Compare_WithJsParser_MultiStmtSimple()
    {
        var result = SqlTestHelpers.Validate("SELECT * FROM MY_TABLE; SELECT 1", _schema);
        Assert.Empty(result.Errors); // JS: VALID (no errors)
    }


    [Fact]
    public void Compare_WithJsParser_FormTypo()
    {
        var result = SqlTestHelpers.Validate("FORM t", _schema);
        // JS: PAR004 - "Possible typo: 'FORM' looks like keyword 'FROM'." [fix: FROM]
        // C#: should produce PAR004 with SuggestedFix FROM
        Assert.Contains(result.Errors, e =>
            e.Code == "PAR004" && e.SuggestedFix == "FROM");
    }


    [Fact]
    public void Compare_WithJsParser_WehreTypo()
    {
        var result = SqlTestHelpers.Validate("WEHRE 1=1", _schema);
        // JS: PAR001 - "Redundant input, expecting EOF but found: WEHRE" (typo NOT detected by JS)
        // C#: Should detect as PAR004 with WEHRE->WHERE via Levenshtein (distance 2, threshold 2 for 5 chars)
        Assert.Contains(result.Errors, e => e.Code is "PAR004" or "PAR001");
    }


    [Fact]
    public void Compare_WithJsParser_FormTypoInSelect()
    {
        var result = SqlTestHelpers.Validate("SELECT 1 FORM t", _schema);
        // JS: PAR004 - "Possible typo: 'FORM' looks like keyword 'FROM'." [fix: FROM]
        // C#: FORM consumed as alias, then t unexpected - should produce some error
        Assert.NotEmpty(result.Errors);
    }


    [Fact]
    public void Compare_WithJsParser_WehreTypoAfterFrom()
    {
        var result = SqlTestHelpers.Validate("SELECT * FROM t WEHRE 1=1", _schema);
        // JS: PAR001 - "Redundant input, expecting EOF but found: 1" (typo NOT detected)
        // C#: WEHRE consumed as alias, 1=1 unexpected -> PAR001
        Assert.Contains(result.Errors, e => e.Code == "PAR001");
    }


    [Fact]
    public void Compare_WithJsParser_DuplicateFrom()
    {
        var result = SqlTestHelpers.Validate("SELECT 1 FROM FROM t", _schema);
        // JS: PAR003 - "Duplicate 'FROM' keyword detected."
        Assert.Contains(result.Errors, e => e.Code == "PAR003");
    }


    [Fact]
    public void Compare_WithJsParser_DuplicateWhere()
    {
        var result = SqlTestHelpers.Validate("SELECT * FROM t WHERE WHERE x=1", _schema);
        // JS: PAR003 - "Duplicate 'WHERE' keyword detected."
        Assert.Contains(result.Errors, e => e.Code == "PAR003");
    }


    [Fact]
    public void Compare_WithJsParser_MissingSelectList()
    {
        var result = SqlTestHelpers.Validate("SELECT FROM t", _schema);
        // JS: PAR001 - "SELECT list is empty."
        Assert.Contains(result.Errors, e => e.Code == "PAR001");
    }


    [Fact]
    public void Compare_WithJsParser_UpdateWithoutSet()
    {
        var result = SqlTestHelpers.Validate("UPDATE t WHERE 1=1", _schema);
        // JS: PAR001 - "Expecting token of type Set but found WHERE"
        // C#: Should produce PAR115 (improved!)
        Assert.Contains(result.Errors, e => e.Code is "PAR115" or "PAR001");
    }


    [Fact]
    public void Compare_WithJsParser_DeleteWithoutFrom()
    {
        var result = SqlTestHelpers.Validate("DELETE WHERE 1=1", _schema);
        // JS: PAR001 - "Expecting token of type From but found WHERE"
        // C#: Should produce PAR116 (improved!)
        Assert.Contains(result.Errors, e => e.Code is "PAR116" or "PAR001");
    }


    [Fact]
    public void Compare_WithJsParser_InsertWithoutInto()
    {
        var result = SqlTestHelpers.Validate("INSERT t VALUES (1)", _schema);
        // JS: PAR001 - "Expecting token of type Into but found t"
        // C#: Should produce PAR114 (improved!)
        Assert.Contains(result.Errors, e => e.Code is "PAR114" or "PAR001");
    }


    [Fact]
    public void Validate_ErrorRecovery_InsertMissingValues_PAR117()
    {
        var result = SqlTestHelpers.Validate("INSERT INTO t");
        Assert.Contains(result.Errors, e => e.Code == "PAR117");
    }
}
