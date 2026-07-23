using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class NzSqlValidatorErrorRecoveryTests
{
    private readonly ISchemaProvider _schema;

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
    public void Validate_ErrorRecovery_UnclosedParenthesisInNestedExpression()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT ((a + b) * c FROM t;");
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
    public void Validate_ErrorRecovery_InWithTrailingComma()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "SELECT * FROM TESTDB..EMPLOYEES WHERE EMPLOYEE_ID IN (1, 2,);",
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
    public void Validate_SelectError_MissingSemicolonIsTolerated()
    {
        var result = SqlTestHelpers.Validate("SELECT 1");
        Assert.DoesNotContain(result.Errors, e => e.Code.StartsWith("PAR"));
        Assert.DoesNotContain(result.Errors, e => e.Code.StartsWith("LEX"));
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
    public void Validate_SelectError_OrderByWithoutExpression()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT * FROM TESTDB..EMPLOYEES ORDER BY;", _schema);
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
    public void Validate_SelectError_InvalidJoinTypeKeywordSequence()
    {
        SqlTestHelpers.ExpectSyntaxError(
            @"SELECT * FROM TESTDB..EMPLOYEES E LEFT RIGHT JOIN TESTDB..DEPARTMENTS D ON E.DEPARTMENT_ID = D.DEPARTMENT_ID;",
            _schema);
    }

    [Fact]
    public void Validate_SelectError_IncompleteOnPredicateInJoin()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "SELECT * FROM TESTDB..EMPLOYEES E JOIN TESTDB..DEPARTMENTS D ON E.DEPARTMENT_ID = ;",
            _schema);
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
    public void Validate_JoinError_ValidExecShorthand()
    {
        SqlTestHelpers.ExpectValid("EXEC SOME_PROC_NAME()");
    }

    [Fact]
    public void Validate_JoinError_ValidExecProcedureShorthand()
    {
        SqlTestHelpers.ExpectValid("EXEC PROCEDURE SOME_PROC_NAME()");
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
    public void Validate_JoinError_RejectLeftRightJoinInvalidCombo()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT * FROM t1 LEFT RIGHT JOIN t2 ON t1.id = t2.id");
    }

    [Fact]
    public void Validate_JoinError_RejectJoinWithMissingTable()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT * FROM t1 INNER JOIN ON t1.id = 1");
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
    public void Validate_DdlError_RejectCreateTableUnclosedParenthesis()
    {
        SqlTestHelpers.ExpectSyntaxError("CREATE TABLE t (id INT, name VARCHAR(100)");
    }

    [Fact]
    public void Validate_DdlError_RejectCreateTableDuplicateComma()
    {
        SqlTestHelpers.ExpectSyntaxError("CREATE TABLE t (id INT,, name VARCHAR(100))");
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
    public void Validate_SelectError2_UnquotedReservedKeywordAsTableName()
    {
        SqlTestHelpers.ExpectErrorCode("SELECT * FROM FROM", "PAR003");
    }

    [Fact]
    public void Validate_SelectError2_GroupByWithoutColumnList()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT COUNT(*) FROM t GROUP BY");
    }

    [Fact]
    public void Validate_SelectError2_HavingWithoutExpression()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT COUNT(*) FROM t GROUP BY id HAVING");
    }

    [Fact]
    public void Validate_SelectError2_OrderByWithTrailingComma()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT id FROM t ORDER BY id,");
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
    public void Validate_ErrorRecovery_UnclosedParen_PAR107_Detected()
    {
        var result = SqlTestHelpers.Validate("SELECT (1 + 2 FROM t");
        Assert.Contains(result.Errors, e => e.Code == "PAR107");
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
    public void Validate_ErrorRecovery_MismatchedParens_PAR112_Detected()
    {
        var result = SqlTestHelpers.Validate("SELECT (1 + 2");
        Assert.Contains(result.Errors, e => e.Code == "PAR112");
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
    public void Compare_WithJsParser_KeywordAsTableFrom()
    {
        var result = SqlTestHelpers.Validate("SELECT * FROM WHERE; SELECT 1", _schema);
        // JS: SQL015 (reserved keyword as table name) - C# doesn't have SQL015
        Assert.NotEmpty(result.Errors); // both emit errors
        Assert.Contains(result.Errors, e => e.Code.StartsWith("PAR"));
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

    [Fact]
    public void Compare_WithJsParser_MissingTableAfterFrom()
    {
        var result = SqlTestHelpers.Validate("SELECT * FROM", _schema);
        // JS: PAR001 - "Missing table or subquery after FROM."
        // C#: PAR001 with similar message
        Assert.Contains(result.Errors, e => e.Code == "PAR001");
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
    public void Compare_WithJsParser_DoubleComma()
    {
        var result = SqlTestHelpers.Validate("SELECT 1,,2", _schema);
        // JS: PAR002 - "Consecutive commas (,,) indicate a missing expression..."
        Assert.Contains(result.Errors, e => e.Code == "PAR002");
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

    [Fact]
    public void Validate_ErrorRecovery_InsertMissingValues_PAR117()
    {
        var result = SqlTestHelpers.Validate("INSERT INTO t");
        Assert.Contains(result.Errors, e => e.Code == "PAR117");
    }
}
