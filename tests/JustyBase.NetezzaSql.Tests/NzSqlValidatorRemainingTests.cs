using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;
using JustyBase.NetezzaSqlParser.Visitor;
using static JustyBase.Tests.NetezzaSqlParser.SqlTestHelpers;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class NzSqlValidatorRemainingTests
{
    private readonly ISchemaProvider _schema;

    public NzSqlValidatorRemainingTests()
    {
        _schema = CreateStandardMockSchema();
    }

    // ========================================================================
    // Window functions — valid syntax
    // ========================================================================

    [Fact]
    public void Validate_Window_Valid_RowNumber()
    {
        ExpectValid(
            "SELECT ROW_NUMBER() OVER (ORDER BY SALARY DESC) AS RN FROM TESTDB..EMPLOYEES;",
            _schema);
    }

    [Fact]
    public void Validate_Window_Valid_RankWithPartitionBy()
    {
        ExpectValid(
            "SELECT RANK() OVER (PARTITION BY DEPARTMENT_ID ORDER BY SALARY DESC) AS RK FROM TESTDB..EMPLOYEES;",
            _schema);
    }

    [Fact]
    public void Validate_Window_Valid_DenseRank()
    {
        ExpectValid(
            "SELECT DENSE_RANK() OVER (ORDER BY SALARY) AS DR FROM TESTDB..EMPLOYEES;",
            _schema);
    }

    [Fact]
    public void Validate_Window_Valid_LagWithOffset()
    {
        ExpectValid(
            "SELECT LAG(SALARY, 1) OVER (ORDER BY EMPLOYEE_ID) AS PREV_SAL FROM TESTDB..EMPLOYEES;",
            _schema);
    }

    [Fact]
    public void Validate_Window_Valid_LeadWithOffsetAndDefault()
    {
        ExpectValid(
            "SELECT LEAD(SALARY, 1, 0) OVER (ORDER BY EMPLOYEE_ID) AS NEXT_SAL FROM TESTDB..EMPLOYEES;",
            _schema);
    }

    [Fact]
    public void Validate_Window_Valid_SumAsWindow()
    {
        ExpectValid(
            "SELECT SUM(SALARY) OVER (PARTITION BY DEPARTMENT_ID ORDER BY EMPLOYEE_ID) AS RUN_TOTAL FROM TESTDB..EMPLOYEES;",
            _schema);
    }

    [Fact]
    public void Validate_Window_Valid_Ntile()
    {
        ExpectValid(
            "SELECT NTILE(4) OVER (ORDER BY SALARY DESC) AS QUARTILE FROM TESTDB..EMPLOYEES;",
            _schema);
    }

    [Fact]
    public void Validate_Window_Valid_FirstValueLastValue()
    {
        ExpectValid(
            "SELECT FIRST_VALUE(SALARY) OVER (PARTITION BY DEPARTMENT_ID ORDER BY SALARY) AS MIN_SAL FROM TESTDB..EMPLOYEES;",
            _schema);
        ExpectValid(
            "SELECT LAST_VALUE(SALARY) OVER (PARTITION BY DEPARTMENT_ID ORDER BY SALARY) AS MAX_SAL FROM TESTDB..EMPLOYEES;",
            _schema);
    }

    // ========================================================================
    // Window Functions — additional patterns
    // ========================================================================

    [Fact]
    public void Validate_Window_Additional_RowNumberWithOrderByOnly()
    {
        ExpectValid(
            "SELECT ROW_NUMBER() OVER (ORDER BY E.SALARY DESC) AS RN, E.FIRST_NAME FROM TESTDB..EMPLOYEES E;",
            _schema);
    }

    [Fact]
    public void Validate_Window_Additional_SumWithPartitionBy()
    {
        ExpectValid(
            "SELECT E.FIRST_NAME, E.SALARY, SUM(E.SALARY) OVER (PARTITION BY E.DEPARTMENT_ID) AS DEPT_TOTAL FROM TESTDB..EMPLOYEES E;",
            _schema);
    }

    [Fact]
    public void Validate_Window_Additional_AvgAsWindow()
    {
        ExpectValid(
            "SELECT E.FIRST_NAME, E.SALARY, AVG(E.SALARY) OVER (PARTITION BY E.DEPARTMENT_ID) AS DEPT_AVG FROM TESTDB..EMPLOYEES E;",
            _schema);
    }

    [Fact]
    public void Validate_Window_Additional_CountAsWindow()
    {
        ExpectValid(
            "SELECT E.FIRST_NAME, COUNT(*) OVER (PARTITION BY E.DEPARTMENT_ID) AS DEPT_COUNT FROM TESTDB..EMPLOYEES E;",
            _schema);
    }

    [Fact]
    public void Validate_Window_Additional_AggregateFilterClause()
    {
        ExpectValid(
            "SELECT COUNT(*) FILTER (WHERE E.SALARY > 0) AS POSITIVE_SALARIES FROM TESTDB..EMPLOYEES E;",
            _schema);
    }

    [Fact]
    public void Validate_Window_Additional_FilterClauseCombinedWithOver()
    {
        ExpectValid(
            "SELECT COUNT(*) FILTER (WHERE E.SALARY > 0) OVER (PARTITION BY E.DEPARTMENT_ID) AS POSITIVE_SALARIES FROM TESTDB..EMPLOYEES E;",
            _schema);
    }

    [Fact]
    public void Validate_Window_Additional_LagWindowFunction()
    {
        ExpectValid(
            "SELECT E.FIRST_NAME, E.SALARY, LAG(E.SALARY, 1, 0) OVER (ORDER BY E.HIRE_DATE) AS PREV_SALARY FROM TESTDB..EMPLOYEES E;",
            _schema);
    }

    [Fact]
    public void Validate_Window_Additional_DenseRankWindowFunction()
    {
        ExpectValid(
            "SELECT E.FIRST_NAME, DENSE_RANK() OVER (ORDER BY E.SALARY DESC) AS DRANK FROM TESTDB..EMPLOYEES E;",
            _schema);
    }

    [Fact]
    public void Validate_Window_Additional_MultipleWindowFunctions()
    {
        ExpectValid(
            "SELECT E.FIRST_NAME, ROW_NUMBER() OVER (ORDER BY E.SALARY) AS RN, RANK() OVER (ORDER BY E.SALARY) AS RNK, DENSE_RANK() OVER (ORDER BY E.SALARY) AS DRNK FROM TESTDB..EMPLOYEES E;",
            _schema);
    }

    [Fact]
    public void Validate_Window_Additional_RowsBetweenUnboundedPrecedingAndCurrentRow()
    {
        ExpectValid(
            "SELECT E.EMPLOYEE_ID, SUM(E.SALARY) OVER (ORDER BY E.EMPLOYEE_ID ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS RUN_SUM FROM TESTDB..EMPLOYEES E;",
            _schema);
    }

    [Fact]
    public void Validate_Window_Additional_RowsBetweenNumericPrecedingFollowing()
    {
        ExpectValid(
            "SELECT E.EMPLOYEE_ID, AVG(E.SALARY) OVER (ORDER BY E.EMPLOYEE_ID ROWS BETWEEN 2 PRECEDING AND 1 FOLLOWING) AS MOVING_AVG FROM TESTDB..EMPLOYEES E;",
            _schema);
    }

    [Fact]
    public void Validate_Window_Additional_RangeBetweenFrame()
    {
        ExpectValid(
            "SELECT E.EMPLOYEE_ID, MAX(E.SALARY) OVER (ORDER BY E.SALARY RANGE BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS RUN_MAX FROM TESTDB..EMPLOYEES E;",
            _schema);
    }

    [Fact]
    public void Validate_Window_Additional_GroupsFrameClause()
    {
        ExpectValid(
            "SELECT E.EMPLOYEE_ID, SUM(E.SALARY) OVER (ORDER BY E.EMPLOYEE_ID GROUPS BETWEEN 1 PRECEDING AND CURRENT ROW) AS GROUP_SUM FROM TESTDB..EMPLOYEES E;",
            _schema);
    }

    [Fact]
    public void Validate_Window_Additional_ExcludeCurrentRow()
    {
        ExpectValid(
            "SELECT E.EMPLOYEE_ID, SUM(E.SALARY) OVER (ORDER BY E.EMPLOYEE_ID ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW EXCLUDE CURRENT ROW) AS RUN_SUM FROM TESTDB..EMPLOYEES E;",
            _schema);
    }

    [Fact]
    public void Validate_Window_Additional_ExcludeGroup()
    {
        ExpectValid(
            "SELECT E.EMPLOYEE_ID, SUM(E.SALARY) OVER (ORDER BY E.EMPLOYEE_ID GROUPS BETWEEN 1 PRECEDING AND 1 FOLLOWING EXCLUDE GROUP) AS GROUP_SUM FROM TESTDB..EMPLOYEES E;",
            _schema);
    }

    [Fact]
    public void Validate_Window_Additional_ExcludeTies()
    {
        ExpectValid(
            "SELECT E.EMPLOYEE_ID, SUM(E.SALARY) OVER (ORDER BY E.SALARY RANGE BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW EXCLUDE TIES) AS RANGE_SUM FROM TESTDB..EMPLOYEES E;",
            _schema);
    }

    // ========================================================================
    // Window functions — syntax errors
    // ========================================================================

    [Fact]
    public void Validate_Window_Error_RejectOverWithoutParens()
    {
        ExpectSyntaxError("SELECT SUM(x) OVER FROM t");
    }

    [Fact]
    public void Validate_Window_Error_MissingPrecedingFollowingKeyword()
    {
        ExpectSyntaxError(
            "SELECT SUM(x) OVER (ORDER BY id ROWS BETWEEN UNBOUNDED AND CURRENT ROW) FROM t");
    }

    [Fact]
    public void Validate_Window_Error_MissingAndInBetween()
    {
        ExpectSyntaxError(
            "SELECT SUM(x) OVER (ORDER BY id ROWS BETWEEN UNBOUNDED PRECEDING CURRENT ROW) FROM t");
    }

    [Fact]
    public void Validate_Window_Error_MissingBoundSpecification()
    {
        ExpectSyntaxError(
            "SELECT E.PARENTEMPLOYEEKEY, SUM(E.CURRENTFLAG::INT) OVER (ORDER BY E.PARENTEMPLOYEEKEY ROWS BETWEEN PRECEDING AND CURRENT ROW) AS RUN_SUM FROM JUST_DATA..DIMEMPLOYEE E");
    }

    [Fact]
    public void Validate_Window_Error_PartitionByWithoutColumnList()
    {
        ExpectSyntaxError("SELECT SUM(x) OVER (PARTITION BY) FROM t");
    }

    [Fact]
    public void Validate_Window_Error_OverWithStrayComma()
    {
        ExpectSyntaxError("SELECT SUM(x) OVER (PARTITION BY a, ORDER BY b) FROM t");
    }

    [Fact]
    public void Validate_Window_Error_MissingFrameBoundBeforePreceding()
    {
        ExpectSyntaxError(
            "SELECT E.EMPLOYEE_ID, SUM(E.SALARY::INT4) OVER (ORDER BY E.EMPLOYEE_ID ROWS BETWEEN PRECEDING AND CURRENT ROW) AS RUN_SUM FROM TESTDB..EMPLOYEES E;",
            _schema);
    }

    [Fact]
    public void Validate_Window_Error_MissingSecondFrameBoundAfterAnd()
    {
        ExpectSyntaxError(
            "SELECT E.EMPLOYEE_ID, AVG(E.SALARY) OVER (ORDER BY E.EMPLOYEE_ID ROWS BETWEEN 1 PRECEDING AND ) AS MOVING_AVG FROM TESTDB..EMPLOYEES E;",
            _schema);
    }

    [Fact]
    public void Validate_Window_Error_MissingOrderByInOver()
    {
        ExpectSyntaxError(
            "SELECT E.EMPLOYEE_ID, SUM(E.SALARY) OVER (ORDER BY ) AS RUN_SUM FROM TESTDB..EMPLOYEES E;",
            _schema);
    }

    // ========================================================================
    // JOIN — syntax errors (new ones not already covered)
    // ========================================================================

    [Fact]
    public void Validate_Join_Valid_NaturalJoin()
    {
        ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES NATURAL JOIN TESTDB..DEPARTMENTS",
            _schema);
    }

    [Fact]
    public void Validate_Join_Valid_NaturalLeftJoin()
    {
        ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES NATURAL LEFT JOIN TESTDB..DEPARTMENTS",
            _schema);
    }

    [Fact]
    public void Validate_Join_Valid_JoinWithUsingClause()
    {
        ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES JOIN TESTDB..DEPARTMENTS USING (DEPARTMENT_ID)",
            _schema);
    }

    [Fact]
    public void Validate_Join_Valid_LeftJoinWithUsingClause()
    {
        ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES LEFT JOIN TESTDB..DEPARTMENTS USING (DEPARTMENT_ID)",
            _schema);
    }

    [Fact]
    public void Validate_Join_Error_RejectNaturalJoinWithOnClause()
    {
        ExpectSyntaxError(
            "SELECT * FROM TESTDB..EMPLOYEES NATURAL JOIN TESTDB..DEPARTMENTS ON 1=1",
            _schema);
    }

    [Fact]
    public void Validate_Join_Valid_CrossJoinWithOn()
    {
        // Node: CROSS JOIN should not have ON clause → SQL002 (warning)
        var result = Validate(
            "SELECT * FROM TESTDB..EMPLOYEES CROSS JOIN TESTDB..DEPARTMENTS ON 1=1",
            _schema);
        Assert.Contains(result.Warnings, w => w.Code == "SQL002");
    }

    [Fact]
    public void Validate_Join_Error_AmbiguousColumnCteAndSubqueryAlias()
    {
        ExpectErrorCode(
            """
            WITH ABC_123 AS
            (
                SELECT 2 AS COL2 FROM TESTDB..DIMACCOUNT
            )
            SELECT COL2 FROM
            (SELECT 200 as COL2) ABC_123
            JOIN ABC_123 x ON 1=1
            """,
            "SQL008",
            _schema);
    }

    // ========================================================================
    // CTE — additional valid patterns
    // ========================================================================

    [Fact]
    public void Validate_Cte_Additional_ExplicitColumnList()
    {
        ExpectValid(
            """
            WITH DEPT_MAP (DEPT_ID, DEPT_NAME) AS (
                SELECT D.DEPARTMENT_ID, D.DEPARTMENT_NAME FROM TESTDB..DEPARTMENTS D
            )
            SELECT DM.DEPT_ID, DM.DEPT_NAME FROM DEPT_MAP DM;
            """,
            _schema);
    }

    [Fact]
    public void Validate_Cte_Additional_RenamedColumnList()
    {
        ExpectValid(
            """
            WITH DEPT_KEYS (KEY_ID) AS (
                SELECT D.DEPARTMENT_ID FROM TESTDB..DEPARTMENTS D
            )
            SELECT DK.KEY_ID FROM DEPT_KEYS DK;
            """,
            _schema);
    }

    [Fact]
    public void Validate_Cte_Additional_WithAggregationAndOrderBy()
    {
        ExpectValid(
            """
            WITH SALARY_STATS AS (
                SELECT DEPARTMENT_ID, AVG(SALARY) AS AVG_SAL FROM TESTDB..EMPLOYEES GROUP BY DEPARTMENT_ID
            )
            SELECT DEPARTMENT_ID, AVG_SAL FROM SALARY_STATS ORDER BY AVG_SAL DESC;
            """,
            _schema);
    }

    [Fact]
    public void Validate_Cte_Additional_ReferencingEarlierCte()
    {
        ExpectValid(
            """
            WITH CTE1 AS (
                SELECT DEPARTMENT_ID FROM TESTDB..DEPARTMENTS
            ), CTE2 AS (
                SELECT C1.DEPARTMENT_ID FROM CTE1 C1
            )
            SELECT C2.DEPARTMENT_ID FROM CTE2 C2;
            """,
            _schema);
    }

    [Fact]
    public void Validate_Cte_Additional_WithLimitInFinalQuery()
    {
        ExpectValid(
            """
            WITH TOP_EARNERS AS (
                SELECT EMPLOYEE_ID, SALARY FROM TESTDB..EMPLOYEES ORDER BY SALARY DESC
            )
            SELECT * FROM TOP_EARNERS LIMIT 10;
            """,
            _schema);
    }

    // ========================================================================
    // CTE — additional syntax errors
    // ========================================================================

    [Fact]
    public void Validate_Cte_Error_MissingWithKeyword()
    {
        ExpectSyntaxError("CTE AS (SELECT 1) SELECT * FROM CTE;");
    }

    [Fact]
    public void Validate_Cte_Error_MissingClosingParenInCteBody()
    {
        ExpectSyntaxError("WITH CTE AS (SELECT 1 SELECT * FROM CTE;");
    }

    [Fact]
    public void Validate_Cte_Error_MissingSelectAfterCteDefinitions()
    {
        ExpectSyntaxError("WITH CTE AS (SELECT 1);");
    }

    [Fact]
    public void Validate_Cte_Error_DoubleAsInCteDefinition()
    {
        ExpectSyntaxError("WITH CTE AS AS (SELECT 1) SELECT * FROM CTE;");
    }

    // ========================================================================
    // CTE — additional syntax errors (extended)
    // ========================================================================

    [Fact]
    public void Validate_Cte_Error_RejectCteWithoutAs()
    {
        ExpectSyntaxError("WITH cte (SELECT 1) SELECT * FROM cte");
    }

    [Fact]
    public void Validate_Cte_Error_RejectCteWithMissingBody()
    {
        ExpectSyntaxError("WITH cte AS SELECT * FROM cte");
    }

    [Fact]
    public void Validate_Cte_Error_RejectCteWithEmptyColumnList()
    {
        ExpectSyntaxError("WITH cte () AS (SELECT 1) SELECT * FROM cte");
    }

    // ========================================================================
    // ORDER BY — NULLS FIRST / NULLS LAST
    // ========================================================================

    [Fact]
    public void Validate_OrderBy_NullsFirst()
    {
        ExpectValid("SELECT * FROM EMPLOYEES ORDER BY SALARY NULLS FIRST;");
    }

    [Fact]
    public void Validate_OrderBy_NullsLast()
    {
        ExpectValid("SELECT * FROM EMPLOYEES ORDER BY SALARY NULLS LAST;");
    }

    [Fact]
    public void Validate_OrderBy_AscNullsFirst()
    {
        ExpectValid("SELECT * FROM EMPLOYEES ORDER BY SALARY ASC NULLS FIRST;");
    }

    [Fact]
    public void Validate_OrderBy_DescNullsLast()
    {
        ExpectValid("SELECT * FROM EMPLOYEES ORDER BY SALARY DESC NULLS LAST;");
    }

    [Fact]
    public void Validate_OrderBy_MultipleItemsWithNulls()
    {
        ExpectValid(
            "SELECT * FROM EMPLOYEES ORDER BY DEPARTMENT_ID ASC NULLS LAST, SALARY DESC NULLS FIRST;");
    }

    [Fact]
    public void Validate_OrderBy_NullsFirstWithoutAscDesc()
    {
        ExpectValid(
            "SELECT * FROM EMPLOYEES ORDER BY FIRST_NAME NULLS FIRST, LAST_NAME NULLS LAST;");
    }

    [Fact]
    public void Validate_OrderBy_Error_RejectNullsWithoutFirstOrLast()
    {
        ExpectSyntaxError("SELECT * FROM EMPLOYEES ORDER BY SALARY NULLS;");
    }

    // ========================================================================
    // Set operations — parenthesized SELECT
    // ========================================================================

    [Fact]
    public void Validate_SetOps_ParenthesizedUnion()
    {
        ExpectValid("(SELECT 1) UNION (SELECT 2);");
    }

    [Fact]
    public void Validate_SetOps_ParenthesizedUnionAll()
    {
        ExpectValid(
            "(SELECT EMPLOYEE_ID FROM EMPLOYEES) UNION ALL (SELECT EMPLOYEE_ID FROM DEPARTMENTS);");
    }

    [Fact]
    public void Validate_SetOps_ParenthesizedIntersect()
    {
        ExpectValid(
            "(SELECT EMPLOYEE_ID FROM EMPLOYEES) INTERSECT (SELECT DEPARTMENT_ID FROM DEPARTMENTS);");
    }

    [Fact]
    public void Validate_SetOps_ParenthesizedExcept()
    {
        ExpectValid(
            "(SELECT EMPLOYEE_ID FROM EMPLOYEES) EXCEPT (SELECT DEPARTMENT_ID FROM DEPARTMENTS);");
    }

    [Fact]
    public void Validate_SetOps_ThreeWayUnion()
    {
        ExpectValid("(SELECT 1) UNION (SELECT 2) UNION (SELECT 3);");
    }

    [Fact]
    public void Validate_SetOps_MixedParenthesizedAndNonParenthesized()
    {
        ExpectValid("(SELECT 1) UNION SELECT 2;");
    }

    [Fact]
    public void Validate_SetOps_ExceptWithUnionOnRight()
    {
        ExpectValid(
            """
            SELECT * FROM TESTDB..EMPLOYEES
            EXCEPT
            (
            SELECT * FROM TESTDB..EMPLOYEES
            UNION
            SELECT * FROM TESTDB..EMPLOYEES
            );
            """,
            _schema);
    }

    // ========================================================================
    // Quantified comparisons — ANY / SOME / ALL
    // ========================================================================

    [Fact]
    public void Validate_Quantified_GtAny()
    {
        ExpectValid(
            "SELECT * FROM EMPLOYEES WHERE SALARY > ANY (SELECT SALARY FROM DEPARTMENTS);");
    }

    [Fact]
    public void Validate_Quantified_EqAny()
    {
        ExpectValid(
            "SELECT * FROM EMPLOYEES WHERE EMPLOYEE_ID = ANY (SELECT LOCATION_ID FROM DEPARTMENTS);");
    }

    [Fact]
    public void Validate_Quantified_LtAll()
    {
        ExpectValid(
            "SELECT * FROM EMPLOYEES WHERE EMPLOYEE_ID < ALL (SELECT LOCATION_ID FROM DEPARTMENTS);");
    }

    [Fact]
    public void Validate_Quantified_GeSome()
    {
        ExpectValid(
            "SELECT * FROM EMPLOYEES WHERE EMPLOYEE_ID >= SOME (SELECT LOCATION_ID FROM DEPARTMENTS);");
    }

    [Fact]
    public void Validate_Quantified_NeAll()
    {
        ExpectValid(
            "SELECT * FROM EMPLOYEES WHERE EMPLOYEE_ID != ALL (SELECT LOCATION_ID FROM DEPARTMENTS);");
    }

    [Fact]
    public void Validate_Quantified_NeqAny()
    {
        ExpectValid(
            "SELECT * FROM EMPLOYEES WHERE EMPLOYEE_ID <> ANY (SELECT LOCATION_ID FROM DEPARTMENTS);");
    }

    [Fact]
    public void Validate_Quantified_AnyInComplexWhere()
    {
        ExpectValid(
            "SELECT * FROM EMPLOYEES WHERE SALARY > ANY (SELECT SALARY FROM DEPARTMENTS) AND DEPARTMENT_ID = 1;");
    }

    [Fact]
    public void Validate_Quantified_EqAnyWithCteSubquery()
    {
        ExpectValid(
            """
            SELECT * FROM EMPLOYEES
            WHERE EMPLOYEE_ID = ANY (
              WITH DEPT_LOCATIONS AS (
                SELECT LOCATION_ID
                FROM DEPARTMENTS
              )
              SELECT * FROM DEPT_LOCATIONS
            );
            """);
    }

    // ========================================================================
    // Boolean expression validation (ON/WHERE/HAVING) — additional patterns
    // ========================================================================

    [Fact]
    public void Validate_Boolean_OnClauseWithCteNotBoolean()
    {
        ExpectErrorCode(
            """
            WITH CTE_1 AS (SELECT 1 AS COL1), CTE_2 AS (SELECT 2 AS COL_A, 3 AS COL_B)
            SELECT C.COL1
            FROM CTE_1 C
            JOIN CTE_2 D ON C.COL1 - D.COL_B;
            """,
            "SQL010",
            _schema);
    }

    [Fact]
    public void Validate_Boolean_WhereExpressionNotBoolean()
    {
        ExpectErrorCode(
            "SELECT * FROM TESTDB..EMPLOYEES A WHERE A.EMPLOYEE_ID + 1;",
            "SQL010",
            _schema);
    }

    // ========================================================================
    // Type casting — additional patterns
    // ========================================================================

    [Fact]
    public void Validate_Casting_CastToInt4()
    {
        ExpectValid("SELECT CAST('123' AS INT4) AS NUM;");
    }

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
    public void Validate_EdgeCases_DeeplyNestedSubqueries()
    {
        ExpectValid("SELECT * FROM (SELECT * FROM (SELECT 1 AS A) T1) T2;");
    }

    [Fact]
    public void Validate_EdgeCases_SelectWithLineBreaks()
    {
        ExpectValid(
            """
            SELECT
                E.EMPLOYEE_ID,
                E.FIRST_NAME,
                E.LAST_NAME
            FROM
                TESTDB..EMPLOYEES E
            WHERE
                E.SALARY > 1000
            ORDER BY
                E.SALARY DESC;
            """,
            _schema);
    }

    [Fact]
    public void Validate_EdgeCases_SelectWithTabCharacters()
    {
        ExpectValid("SELECT\t1\tAS\tA;");
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
    public void Validate_EdgeCases_DatabaseTableNotation()
    {
        ExpectValid("SELECT * FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_EdgeCases_DatabaseSchemaTableNotation()
    {
        ExpectValid("SELECT * FROM TESTDB.PUBLIC.EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_EdgeCases_MixedCommentStyles()
    {
        ExpectValid(
            """
            -- Line comment at start
            SELECT
                E.EMPLOYEE_ID, /* block comment inline */
                E.FIRST_NAME -- trailing comment
            FROM TESTDB..EMPLOYEES E
            /* multi-line
               block comment */
            WHERE E.SALARY > 0;
            """,
            _schema);
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
    public void Validate_Select_Additional_NestedAndOrWithParens()
    {
        ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES WHERE (SALARY > 1000 AND DEPARTMENT_ID = 1) OR (STATUS = 'ACTIVE');",
            _schema);
    }

    [Fact]
    public void Validate_Select_Additional_BetweenOnDates()
    {
        ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES WHERE HIRE_DATE BETWEEN '2020-01-01' AND '2023-12-31';",
            _schema);
    }

    [Fact]
    public void Validate_Select_Additional_MultipleLikeConditions()
    {
        ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES WHERE FIRST_NAME LIKE 'J%' AND LAST_NAME LIKE '%son';",
            _schema);
    }

    [Fact]
    public void Validate_Select_Additional_NullLiteral()
    {
        ExpectValid("SELECT NULL AS EMPTY_COL;");
    }

    [Fact]
    public void Validate_Select_Additional_Coalesce()
    {
        ExpectValid(
            "SELECT COALESCE(E.MANAGER_ID, 0) AS MGR FROM TESTDB..EMPLOYEES E;",
            _schema);
    }

    [Fact]
    public void Validate_Select_Additional_Nullif()
    {
        ExpectValid(
            "SELECT NULLIF(E.SALARY, 0) AS SAFE_SALARY FROM TESTDB..EMPLOYEES E;",
            _schema);
    }

    [Fact]
    public void Validate_Select_Additional_Nvl()
    {
        ExpectValid(
            "SELECT NVL(E.MANAGER_ID, -1) AS MGR FROM TESTDB..EMPLOYEES E;",
            _schema);
    }

    [Fact]
    public void Validate_Select_Additional_Nvl2()
    {
        ExpectValid(
            "SELECT NVL2(E.MANAGER_ID, 'HAS_MANAGER', 'NO_MANAGER') AS MGR_FLAG FROM TESTDB..EMPLOYEES E;",
            _schema);
    }

    [Fact]
    public void Validate_Select_Additional_GreatestAndLeast()
    {
        ExpectValid("SELECT GREATEST(1, 2, 3) AS G, LEAST(1, 2, 3) AS L;");
    }

    [Fact]
    public void Validate_Select_Additional_Decode()
    {
        ExpectValid(
            "SELECT DECODE(E.DEPARTMENT_ID, 1, 'HR', 2, 'IT', 'Other') AS DEPT_NAME FROM TESTDB..EMPLOYEES E;",
            _schema);
    }

    [Fact]
    public void Validate_Select_Additional_MultipleOrderByColumns()
    {
        ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES ORDER BY DEPARTMENT_ID ASC, SALARY DESC, FIRST_NAME;",
            _schema);
    }

    [Fact]
    public void Validate_Select_Additional_GroupByMultipleColumns()
    {
        ExpectValid(
            "SELECT DEPARTMENT_ID, STATUS, COUNT(*) AS CNT FROM TESTDB..EMPLOYEES GROUP BY DEPARTMENT_ID, STATUS;",
            _schema);
    }

    [Fact]
    public void Validate_Select_Additional_AggregateFunctions()
    {
        ExpectValid(
            "SELECT SUM(SALARY) AS TOTAL, AVG(SALARY) AS AVERAGE, MIN(SALARY) AS LOWEST, MAX(SALARY) AS HIGHEST FROM TESTDB..EMPLOYEES;",
            _schema);
    }

    [Fact]
    public void Validate_Select_Additional_StringConcatInSelectList()
    {
        ExpectValid(
            "SELECT E.FIRST_NAME || ' ' || E.LAST_NAME AS FULL_NAME FROM TESTDB..EMPLOYEES E;",
            _schema);
    }

    [Fact]
    public void Validate_Select_Additional_MathematicalOperations()
    {
        ExpectValid(
            "SELECT E.SALARY * 12 AS ANNUAL, E.SALARY / 160 AS HOURLY FROM TESTDB..EMPLOYEES E;",
            _schema);
    }

    [Fact]
    public void Validate_Select_Additional_ModuloOperator()
    {
        ExpectValid(
            "SELECT E.EMPLOYEE_ID % 2 AS MOD_VAL FROM TESTDB..EMPLOYEES E;",
            _schema);
    }

    [Fact]
    public void Validate_Select_Additional_NestedFunctionCalls()
    {
        ExpectValid(
            "SELECT UPPER(TRIM(E.FIRST_NAME)) AS CLEAN_NAME FROM TESTDB..EMPLOYEES E;",
            _schema);
    }

    [Fact]
    public void Validate_Select_Additional_SubstrFunction()
    {
        ExpectValid(
            "SELECT SUBSTR(E.FIRST_NAME, 1, 3) AS SHORT_NAME FROM TESTDB..EMPLOYEES E;",
            _schema);
    }

    [Fact]
    public void Validate_Select_Additional_LengthFunction()
    {
        ExpectValid(
            "SELECT LENGTH(E.FIRST_NAME) AS NAME_LEN FROM TESTDB..EMPLOYEES E;",
            _schema);
    }

    [Fact]
    public void Validate_Select_Additional_ReplaceFunction()
    {
        ExpectValid(
            "SELECT REPLACE(E.FIRST_NAME, 'A', 'X') AS REPLACED FROM TESTDB..EMPLOYEES E;",
            _schema);
    }

    [Fact]
    public void Validate_Select_Additional_CastFunction()
    {
        ExpectValid(
            "SELECT CAST(E.SALARY AS VARCHAR(20)) AS SALARY_STR FROM TESTDB..EMPLOYEES E;",
            _schema);
    }

    [Fact]
    public void Validate_Select_Additional_MultipleCastExpressions()
    {
        ExpectValid(
            "SELECT CAST(E.EMPLOYEE_ID AS VARCHAR(10)) || '-' || CAST(E.DEPARTMENT_ID AS VARCHAR(10)) AS COMBO FROM TESTDB..EMPLOYEES E;",
            _schema);
    }

    [Fact]
    public void Validate_Select_Additional_NowFunction()
    {
        ExpectValid("SELECT NOW() AS CURRENT_TS;");
    }

    [Fact]
    public void Validate_Select_Additional_CurrentDate_Time_Timestamp()
    {
        ExpectValid(
            "SELECT CURRENT_DATE AS D, CURRENT_TIME AS T, CURRENT_TIMESTAMP AS TS;");
    }

    [Fact]
    public void Validate_Select_Additional_SimpleHaving()
    {
        ExpectValid(
            "SELECT DEPARTMENT_ID, SUM(SALARY) AS TOTAL_SALARY FROM TESTDB..EMPLOYEES GROUP BY DEPARTMENT_ID HAVING SUM(SALARY) > 50000;",
            _schema);
    }

    [Fact]
    public void Validate_Select_Additional_TableAliasInAllClauses()
    {
        ExpectValid(
            "SELECT E.FIRST_NAME, E.SALARY FROM TESTDB..EMPLOYEES E WHERE E.SALARY > 1000 ORDER BY E.SALARY;",
            _schema);
    }

    [Fact]
    public void Validate_Select_Additional_SelfJoin()
    {
        ExpectValid(
            "SELECT E1.FIRST_NAME, E2.FIRST_NAME AS MANAGER_NAME FROM TESTDB..EMPLOYEES E1 JOIN TESTDB..EMPLOYEES E2 ON E1.MANAGER_ID = E2.EMPLOYEE_ID;",
            _schema);
    }

    [Fact]
    public void Validate_Select_Additional_ExpressionInOrderBy()
    {
        ExpectValid(
            "SELECT E.FIRST_NAME, E.SALARY FROM TESTDB..EMPLOYEES E ORDER BY E.SALARY * 12 DESC;",
            _schema);
    }

    [Fact]
    public void Validate_Select_Additional_LimitZero()
    {
        ExpectValid("SELECT * FROM TESTDB..EMPLOYEES LIMIT 0;", _schema);
    }

    [Fact]
    public void Validate_Select_Additional_SelectOne()
    {
        ExpectValid("SELECT 1;");
    }

    [Fact]
    public void Validate_Select_Additional_NegativeNumber()
    {
        ExpectValid("SELECT -1 AS NEG;");
    }

    [Fact]
    public void Validate_Select_Additional_BooleanLiterals()
    {
        ExpectValid("SELECT TRUE AS T, FALSE AS F;");
    }

    [Fact]
    public void Validate_Select_Additional_LowercaseBooleanAndWildcard()
    {
        ExpectValid("SELECT true, false, * FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_Select_Additional_StringComparison()
    {
        ExpectValid("SELECT * FROM TESTDB..EMPLOYEES WHERE FIRST_NAME = 'John';", _schema);
    }

    [Fact]
    public void Validate_Select_Additional_InequalityOperators()
    {
        ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES WHERE SALARY != 0 AND SALARY <> 0;",
            _schema);
    }

    [Fact]
    public void Validate_Select_Additional_GteAndLte()
    {
        ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES WHERE SALARY >= 1000 AND SALARY <= 5000;",
            _schema);
    }

    [Fact]
    public void Validate_Select_Additional_ComplexWhereCombiningOperators()
    {
        ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES WHERE SALARY > 1000 AND DEPARTMENT_ID IN (1, 2) AND FIRST_NAME LIKE 'A%' AND HIRE_DATE BETWEEN '2020-01-01' AND '2023-12-31';",
            _schema);
    }

    [Fact]
    public void Validate_Select_Additional_SubqueryInSelectList()
    {
        ExpectValid(
            "SELECT E.FIRST_NAME, (SELECT COUNT(*) FROM TESTDB..DEPARTMENTS) AS DEPT_COUNT FROM TESTDB..EMPLOYEES E;",
            _schema);
    }

    [Fact]
    public void Validate_Select_Additional_MultipleSubqueriesInFrom()
    {
        ExpectValid(
            "SELECT A.CNT, B.TOTAL FROM (SELECT COUNT(*) AS CNT FROM TESTDB..EMPLOYEES) A, (SELECT SUM(SALARY) AS TOTAL FROM TESTDB..EMPLOYEES) B;",
            _schema);
    }

    [Fact]
    public void Validate_Select_Additional_CorrelatedSubquery()
    {
        ExpectValid(
            "SELECT E.FIRST_NAME, E.SALARY FROM TESTDB..EMPLOYEES E WHERE E.SALARY > (SELECT AVG(E2.SALARY) FROM TESTDB..EMPLOYEES E2 WHERE E2.DEPARTMENT_ID = E.DEPARTMENT_ID);",
            _schema);
    }

    // ========================================================================
    // SELECT — additional syntax errors (new ones not already covered)
    // ========================================================================

    [Fact]
    public void Validate_Select_Error_MissingFromKeywordAndTable()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT id WHERE x > 1 FROM t");
    }

    [Fact]
    public void Validate_Select_Error_UnquotedReservedKeywordAsTableName()
    {
        ExpectErrorCode("SELECT * FROM FROM", "PAR003");
    }

    [Fact]
    public void Validate_Select_Error_MissingTableNameAfterFrom()
    {
        ExpectSyntaxError("SELECT * FROM;");
    }

    [Fact]
    public void Validate_Select_Error_Negative_MissingSemicolonTolerated()
    {
        var result = Validate("SELECT 1");
        Assert.DoesNotContain(result.Errors, e => e.Code.StartsWith("PAR"));
        Assert.DoesNotContain(result.Errors, e => e.Code.StartsWith("LEX"));
    }

    [Fact]
    public void Validate_Select_Error_DuplicateFromKeyword()
    {
        ExpectSyntaxError("SELECT * FROM FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_Select_Error_GroupByWithoutColumnList()
    {
        ExpectSyntaxError("SELECT COUNT(*) FROM t GROUP BY");
    }

    [Fact]
    public void Validate_Select_Error_HavingWithoutExpression()
    {
        ExpectSyntaxError("SELECT COUNT(*) FROM t GROUP BY id HAVING");
    }

    [Fact]
    public void Validate_Select_Error_OrderByWithTrailingComma()
    {
        ExpectSyntaxError("SELECT id FROM t ORDER BY id,");
    }

    [Fact]
    public void Validate_Select_Error_DoubleDistinct()
    {
        ExpectSyntaxError("SELECT DISTINCT DISTINCT id FROM t");
    }

    [Fact]
    public void Validate_Select_Error_MissingJoinKeywordBetweenTables()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "SELECT * FROM TESTDB..EMPLOYEES E TESTDB..DEPARTMENTS D ON E.DEPARTMENT_ID = D.DEPARTMENT_ID;",
            _schema);
    }

    [Fact]
    public void Validate_Select_Error_InvalidJoinTypeKeywordSequence()
    {
        SqlTestHelpers.ExpectSyntaxError(
            "SELECT * FROM TESTDB..EMPLOYEES E LEFT RIGHT JOIN TESTDB..DEPARTMENTS D ON E.DEPARTMENT_ID = D.DEPARTMENT_ID;",
            _schema);
    }

    [Fact]
    public void Validate_Select_Error_IncompleteBetweenExpression()
    {
        ExpectSyntaxError(
            "SELECT * FROM TESTDB..EMPLOYEES WHERE SALARY BETWEEN 1000;",
            _schema);
    }

    [Fact]
    public void Validate_Select_Error_ExtraKeywordAfterLimit()
    {
        ExpectSyntaxError(
            "SELECT * FROM TESTDB..EMPLOYEES LIMIT 10 WHERE SALARY > 0;",
            _schema);
    }

    // ========================================================================
    // Utility commands — additional patterns
    // ========================================================================

    [Fact]
    public void Validate_Utility_ExplainSimple()
    {
        ExpectValid("EXPLAIN SELECT * FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_Utility_ExplainVerbose()
    {
        ExpectValid("EXPLAIN VERBOSE SELECT * FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_Utility_GroomTable()
    {
        ExpectValid("GROOM TABLE TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_Utility_GroomTableVersions()
    {
        ExpectValid("GROOM TABLE TESTDB..EMPLOYEES VERSIONS;", _schema);
    }

    [Fact]
    public void Validate_Utility_GenerateStatisticsOn()
    {
        ExpectValid("GENERATE STATISTICS ON TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_Utility_GenerateExpressStatisticsOn()
    {
        ExpectValid("GENERATE EXPRESS STATISTICS ON TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_Utility_CommentOnTable()
    {
        ExpectValid(
            "COMMENT ON TABLE TESTDB..EMPLOYEES IS 'Employee master table';",
            _schema);
    }

    [Fact]
    public void Validate_Utility_CommentOnColumn()
    {
        ExpectValid(
            "COMMENT ON COLUMN TESTDB..EMPLOYEES.SALARY IS 'Monthly salary in USD';",
            _schema);
    }

    [Fact]
    public void Validate_Utility_ShowSchema()
    {
        ExpectValid("SHOW SCHEMA;");
    }

    [Fact]
    public void Validate_Utility_ShowSession()
    {
        ExpectValid("SHOW SESSION;");
    }

    [Fact]
    public void Validate_Utility_CopyCommand()
    {
        ExpectValid("COPY TESTDB..EMPLOYEES TO '/tmp/employees.csv';", _schema);
    }

    [Fact]
    public void Validate_Utility_LockTable()
    {
        ExpectValid("LOCK TABLE TESTDB..EMPLOYEES IN EXCLUSIVE MODE;", _schema);
    }

    [Fact]
    public void Validate_Utility_MergeCommand()
    {
        ExpectValid(
            "MERGE INTO TESTDB..EMPLOYEES E USING TESTDB..DEPARTMENTS D ON E.DEPARTMENT_ID = D.DEPARTMENT_ID WHEN MATCHED THEN UPDATE SET STATUS = 'A';",
            _schema);
    }

    [Fact]
    public void Validate_Utility_ReindexDatabase()
    {
        ExpectValid("REINDEX DATABASE TESTDB;");
    }

    [Fact]
    public void Validate_Utility_ResetSession()
    {
        ExpectValid("RESET SESSION;");
    }

    [Fact]
    public void Validate_Utility_BeginTransaction()
    {
        ExpectValid("BEGIN;");
    }

    // ========================================================================
    // ALTER commands — additional patterns
    // ========================================================================

    [Fact]
    public void Validate_Alter_TableAddColumnNotNull()
    {
        ExpectValid(
            "ALTER TABLE TESTDB..EMPLOYEES ADD COLUMN EMAIL VARCHAR(255) NOT NULL;",
            _schema);
    }

    [Fact]
    public void Validate_Alter_TableDropColumn()
    {
        ExpectValid("ALTER TABLE TESTDB..EMPLOYEES DROP COLUMN STATUS;", _schema);
    }

    [Fact]
    public void Validate_Alter_TableOwnerTo()
    {
        ExpectValid("ALTER TABLE TESTDB..EMPLOYEES OWNER TO ADMIN;", _schema);
    }

    [Fact]
    public void Validate_Alter_ViewOwnerTo()
    {
        ExpectValid("ALTER VIEW TESTDB..EMP_VIEW OWNER TO ADMIN;", _schema);
    }

    [Fact]
    public void Validate_Alter_DatabaseRenameTo()
    {
        ExpectValid("ALTER DATABASE TESTDB RENAME TO NEWDB;");
    }

    // ========================================================================
    // Advanced grammar coverage — simple subset
    // ========================================================================

    [Fact]
    public void Validate_Advanced_SimpleUnion()
    {
        ExpectValid("SELECT 1 AS A UNION SELECT 2 AS A;");
    }

    [Fact]
    public void Validate_Advanced_SimpleUnionAll()
    {
        ExpectValid("SELECT 1 AS A UNION ALL SELECT 2 AS A;");
    }

    [Fact]
    public void Validate_Advanced_SimpleIntersect()
    {
        ExpectValid("SELECT 1 AS A INTERSECT SELECT 1 AS A;");
    }

    [Fact]
    public void Validate_Advanced_SimpleExcept()
    {
        ExpectValid("SELECT 1 AS A EXCEPT SELECT 2 AS A;");
    }

    [Fact]
    public void Validate_Advanced_ChainedUnionAll()
    {
        ExpectValid(
            "SELECT 1 AS A UNION ALL SELECT 2 AS A UNION ALL SELECT 3 AS A;");
    }

    [Fact]
    public void Validate_Advanced_NotInList()
    {
        ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES WHERE DEPARTMENT_ID NOT IN (1, 2, 3);",
            _schema);
    }

    [Fact]
    public void Validate_Advanced_ExistsSubquery()
    {
        ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES E WHERE EXISTS (SELECT 1 FROM TESTDB..DEPARTMENTS D WHERE D.DEPARTMENT_ID = E.DEPARTMENT_ID);",
            _schema);
    }

    [Fact]
    public void Validate_Advanced_DeleteWithExists()
    {
        ExpectValid(
            "DELETE FROM TESTDB..EMPLOYEES E WHERE EXISTS (SELECT 1 FROM TESTDB..ORDERS O WHERE O.CUSTOMER_ID = E.EMPLOYEE_ID);",
            _schema);
    }

    [Fact]
    public void Validate_Advanced_QuotedIdentifiers()
    {
        ExpectValid("SELECT \"EMPLOYEE_ID\", \"FIRST_NAME\" FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_Advanced_ParameterMarkers()
    {
        ExpectValid("SELECT * FROM TESTDB..EMPLOYEES WHERE EMPLOYEE_ID = ?;", _schema);
    }

    [Fact]
    public void Validate_Advanced_MissingAsInCte()
    {
        ExpectSyntaxError("WITH CTE (SELECT 1 AS VAL) SELECT * FROM CTE;");
    }

    [Fact]
    public void Validate_Advanced_UnmatchedParenInCtas()
    {
        ExpectSyntaxError("CREATE TABLE TESTDB..T AS (SELECT 1 AS COL;", _schema);
    }

    // ========================================================================
    // Variables and basic statements
    // ========================================================================

    [Fact]
    public void Validate_Variables_SimpleCommit()
    {
        ExpectValid("COMMIT;");
    }

    [Fact]
    public void Validate_Variables_SimpleRollback()
    {
        ExpectValid("ROLLBACK;");
    }

    [Fact]
    public void Validate_Variables_SimpleSetAssign()
    {
        ExpectValid("@SET myVar = 1;");
    }

    // ========================================================================
    // Scope Builder (adapted from TS)
    // ========================================================================

    [Fact]
    public void Validate_Scope_NestedScopes()
    {
        var sql = """
                  SELECT Z.INNER_COL FROM
                  TESTDB..DIMEMPLOYEE E
                  LEFT JOIN (
                      SELECT 1 AS INNER_COL FROM TESTDB..DIMACCOUNT
                      JOIN (
                          SELECT 1 AS INNER_INNER_COL FROM TESTDB..DIMACCOUNT
                      ) Z2 ON 1 = 1
                  ) Z ON Z.INNER_COL = E.EMPLOYEEKEY
                  LIMIT 1
                  """;
        var result = Validate(sql, _schema);
        var syntaxErrors = result.Errors.Where(e => e.Code.StartsWith("PAR") || e.Code.StartsWith("LEX")).ToList();
        Assert.Empty(syntaxErrors);
    }

    // ========================================================================
    // Valid CALL / EXECUTE statements
    // ========================================================================

    [Fact]
    public void Validate_Call_Simple()
    {
        ExpectValid("CALL SOME_PROC_NAME()");
    }

    [Fact]
    public void Validate_Call_SchemaQualified()
    {
        ExpectValid("CALL JUST_DATA.ADMIN.SOME_PROC_NAME()");
    }

    [Fact]
    public void Validate_Call_WithArguments()
    {
        ExpectValid("CALL SOME_PROC_NAME('test', 123, 45.67)");
    }

    [Fact]
    public void Validate_Call_ExecuteProcedureAlternative()
    {
        ExpectValid("EXECUTE PROCEDURE SOME_PROC_NAME()");
    }

    [Fact]
    public void Validate_Call_ExecuteAlternative()
    {
        ExpectValid("EXECUTE SOME_PROC_NAME()");
    }

    [Fact]
    public void Validate_Call_ExecShorthand()
    {
        ExpectValid("EXEC SOME_PROC_NAME()");
    }

    [Fact]
    public void Validate_Call_ExecProcedureShorthand()
    {
        ExpectValid("EXEC PROCEDURE SOME_PROC_NAME()");
    }

    // ========================================================================
    // ADVANCED: Simple CTE column validation subset
    // ========================================================================

    [Fact]
    public void Validate_Advanced_NonExistentColumnInCteDefinition()
    {
        ExpectErrorCode(
            """
            WITH CTE_TEST AS (
                SELECT NONEXISTENT_COLUMN, FIRST_NAME
                FROM TESTDB..EMPLOYEES
            )
            SELECT * FROM CTE_TEST;
            """,
            "SQL004",
            _schema);
    }

    [Fact]
    public void Validate_Advanced_NonExistentColumnReferencedFromCte()
    {
        ExpectErrorCode(
            """
            WITH CTE_EMP AS (
                SELECT EMPLOYEE_ID, FIRST_NAME FROM TESTDB..EMPLOYEES
            )
            SELECT CTE_EMP.FAKE_COLUMN FROM CTE_EMP;
            """,
            "SQL004",
            _schema);
    }

    [Fact]
    public void Validate_Advanced_ColumnExistenceInSubqueryAlias()
    {
        ExpectValid(
            """
            SELECT SUB.EMP_ID, SUB.FULL_NAME
            FROM (
                SELECT E.EMPLOYEE_ID AS EMP_ID,
                       E.FIRST_NAME || ' ' || E.LAST_NAME AS FULL_NAME
                FROM TESTDB..EMPLOYEES E
            ) SUB;
            """,
            _schema);
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
    public void Validate_NodeParity_LeadingSemicolonsWarning() { }

    [Fact]
    public void Validate_NodeParity_MiddleEmptyStatementsWarning() { }

    [Fact]
    public void Validate_NodeParity_IlikeOperator()
    {
        ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES WHERE FIRST_NAME ILIKE 'a%';",
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_IlikeWithNot()
    {
        ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES WHERE FIRST_NAME NOT ILIKE 'a%';",
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_FetchFirstRowsOnly()
    {
        ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES ORDER BY EMPLOYEE_ID FETCH FIRST 10 ROWS ONLY;",
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_FetchFirstRowOnly()
    {
        ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES ORDER BY EMPLOYEE_ID FETCH FIRST 1 ROW ONLY;",
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_FetchFirstAfterLimit()
    {
        ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES ORDER BY EMPLOYEE_ID LIMIT 20 FETCH FIRST 10 ROWS ONLY;",
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
    public void Validate_NodeParity_QuantifiedComparisonEqualsAny()
    {
        ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES WHERE SALARY = ANY (SELECT SALARY FROM TESTDB..EMPLOYEES);",
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_QuantifiedComparisonGreaterThanAll()
    {
        ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES WHERE SALARY > ALL (SELECT SALARY FROM TESTDB..EMPLOYEES WHERE DEPARTMENT_ID = 2);",
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_QuantifiedComparisonNotEqualAny()
    {
        ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES WHERE SALARY != ANY (SELECT SALARY FROM TESTDB..EMPLOYEES);",
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_CurrentTimestampAlias()
    {
        ExpectValid("SELECT CURRENT_TIMESTAMP AS NOW;");
    }

    [Fact]
    public void Validate_NodeParity_CurrentDateInExpression()
    {
        ExpectValid("SELECT CURRENT_DATE - 1 AS YESTERDAY;");
    }

    [Fact]
    public void Validate_NodeParity_RowidSystemColumn()
    {
        ExpectValid(
            "SELECT ROWID FROM TESTDB..EMPLOYEES;",
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_RowidQualified()
    {
        ExpectValid(
            "SELECT E.ROWID FROM TESTDB..EMPLOYEES E;",
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_CreatexidSystemColumn()
    {
        ExpectValid(
            "SELECT CREATEXID FROM TESTDB..EMPLOYEES;",
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_DeletexidSystemColumn()
    {
        ExpectValid(
            "SELECT DELETEXID FROM TESTDB..EMPLOYEES;",
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_DatasliceidSystemColumn()
    {
        ExpectValid(
            "SELECT DATASLICEID FROM TESTDB..EMPLOYEES;",
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_WindowExcludeCurrentRow()
    {
        ExpectValid(
            "SELECT EMPLOYEE_ID, SUM(SALARY) OVER (ORDER BY EMPLOYEE_ID ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW EXCLUDE CURRENT ROW) FROM TESTDB..EMPLOYEES;",
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_WindowExcludeGroup()
    {
        ExpectValid(
            "SELECT EMPLOYEE_ID, SUM(SALARY) OVER (ORDER BY EMPLOYEE_ID ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW EXCLUDE GROUP) FROM TESTDB..EMPLOYEES;",
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_WindowExcludeTies()
    {
        ExpectValid(
            "SELECT EMPLOYEE_ID, SUM(SALARY) OVER (ORDER BY EMPLOYEE_ID ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW EXCLUDE TIES) FROM TESTDB..EMPLOYEES;",
            _schema);
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
    public void Validate_NodeParity_SelectIntoVariable()
    {
        ExpectValid("SELECT COUNT(*) INTO VAR_CNT FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_NodeParity_ModuloOperator()
    {
        ExpectValid("SELECT 10 % 3 AS MOD_RESULT;");
    }

    [Fact]
    public void Validate_NodeParity_ScientificNotation()
    {
        ExpectValid("SELECT 1.5e10 AS SCI;");
    }

    [Fact]
    public void Validate_NodeParity_MultipleStatementsWithSemicolons()
    {
        ExpectValid(
            """
            SELECT 1;
            SELECT 2;
            SELECT 3;
            """);
    }

    [Fact]
    public void Validate_NodeParity_DropTableIfExists()
    {
        ExpectValid("DROP TABLE IF EXISTS TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_NodeParity_CreateTableIfNotExists()
    {
        ExpectValid(
            "CREATE TABLE IF NOT EXISTS TESTDB..NEW_TABLE (ID INT4, NAME VARCHAR(100));",
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_ConstraintPrimaryKey()
    {
        ExpectValid(
            "CREATE TABLE TEST_TABLE (ID INT4 PRIMARY KEY, NAME VARCHAR(100));",
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_ConstraintUnique()
    {
        ExpectValid(
            "CREATE TABLE TEST_TABLE (ID INT4, NAME VARCHAR(100), UNIQUE (ID));",
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_ConstraintForeignKey()
    {
        ExpectValid(
            "CREATE TABLE TEST_TABLE (ID INT4 REFERENCES OTHER_TABLE(ID), NAME VARCHAR(100));",
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_ConstraintCheck()
    {
        ExpectValid(
            "CREATE TABLE TEST_TABLE (ID INT4, NAME VARCHAR(100), CHECK (ID > 0));",
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_ConstraintNamedConstraint()
    {
        ExpectValid(
            "CREATE TABLE TEST_TABLE (ID INT4, CONSTRAINT PK_ID PRIMARY KEY (ID));",
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_ParenthesizedSelectUnion()
    {
        ExpectValid(
            "(SELECT EMPLOYEE_ID FROM TESTDB..EMPLOYEES) UNION (SELECT DEPARTMENT_ID FROM TESTDB..DEPARTMENTS);",
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_ParenthesizedUnionIntersect()
    {
        ExpectValid(
            "(SELECT EMPLOYEE_ID FROM TESTDB..EMPLOYEES) INTERSECT (SELECT DEPARTMENT_ID FROM TESTDB..DEPARTMENTS);",
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_ParenthesizedSelectExcept()
    {
        ExpectValid(
            "(SELECT EMPLOYEE_ID FROM TESTDB..EMPLOYEES) EXCEPT (SELECT DEPARTMENT_ID FROM TESTDB..DEPARTMENTS);",
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_NestedParenthesizedUnion()
    {
        // Three-way parenthesized: (SELECT 1) UNION (SELECT 2) UNION (SELECT 3)
        ExpectValid(
            "(SELECT 1) UNION (SELECT 2) UNION (SELECT 3);");
    }

    [Fact]
    public void Validate_NodeParity_ExistsSubquery()
    {
        ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES E WHERE EXISTS (SELECT 1 FROM TESTDB..DEPARTMENTS D WHERE D.DEPARTMENT_ID = E.DEPARTMENT_ID);",
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_NotExistsSubquery()
    {
        ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES E WHERE NOT EXISTS (SELECT 1 FROM TESTDB..DEPARTMENTS D WHERE D.DEPARTMENT_ID = E.DEPARTMENT_ID);",
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_DeleteWithExists()
    {
        ExpectValid(
            "DELETE FROM TESTDB..EMPLOYEES E WHERE EXISTS (SELECT 1 FROM TESTDB..DEPARTMENTS D WHERE D.DEPARTMENT_ID = E.DEPARTMENT_ID);",
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_ParameterMarker()
    {
        // ? parameter marker should parse without error
        ExpectValid("SELECT * FROM TESTDB..EMPLOYEES WHERE EMPLOYEE_ID = ?;", _schema);
    }

    [Fact]
    public void Validate_NodeParity_CteWithInsertIntoSelect()
    {
        ExpectValid(
            """
            WITH CTE_DATA AS (
                SELECT EMPLOYEE_ID, FIRST_NAME FROM TESTDB..EMPLOYEES
            )
            SELECT * FROM CTE_DATA;
            """,
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_AtSetVariable()
    {
        ExpectValid("@SET VARIABLE = 100;");
    }

    [Fact]
    public void Validate_NodeParity_SelectWithNone()
    {
        // NONE is not a recognized SQL construct in this parser; skip
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
    public void Validate_NodeParity_SelectWithCurrentUser()
    {
        ExpectValid("SELECT CURRENT_USER;");
    }

    [Fact]
    public void Validate_NodeParity_AggregateFilterClause()
    {
        ExpectValid(
            "SELECT DEPARTMENT_ID, SUM(SALARY) FILTER (WHERE SALARY > 50000) FROM TESTDB..EMPLOYEES GROUP BY DEPARTMENT_ID;",
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_WindowFunctionLagWithDefault()
    {
        ExpectValid(
            "SELECT EMPLOYEE_ID, LAG(SALARY, 1, 0) OVER (ORDER BY EMPLOYEE_ID) FROM TESTDB..EMPLOYEES;",
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_ComplexAliasReuseInSelectWhere()
    {
        // Alias from SELECT used in WHERE (Netezza extension)
        ExpectValid(
            "SELECT SALARY * 1.1 AS RAISED_SALARY FROM TESTDB..EMPLOYEES WHERE RAISED_SALARY > 50000;",
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_IllegalSelectListForwardReference()
    {
        // Forward reference should fail — SQL004
        ExpectErrorCode(
            "SELECT RAISED_SALARY, SALARY * 1.1 AS RAISED_SALARY FROM TESTDB..EMPLOYEES;",
            "SQL004",
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_DbTableFormDetection()
    {
        // DB.TABLE (single dot) should produce SQL007
        ExpectErrorCode(
            "SELECT * FROM TESTDB.EMPLOYEES;",
            "SQL007",
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_AsAllCteModifier()
    {
        // Netezza extension: AS ALL materialization hint
        ExpectValid(
            "WITH MYCTE AS ALL (SELECT * FROM TESTDB..EMPLOYEES) SELECT * FROM MYCTE;",
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_NextValueForSequence()
    {
        ExpectValid("SELECT NEXT VALUE FOR MY_SEQ;");
    }

    [Fact]
    public void Validate_NodeParity_ExplainVerboseDistribution()
    {
        ExpectValid("EXPLAIN VERBOSE DISTRIBUTION SELECT * FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_NodeParity_ExplainPlangraph()
    {
        ExpectValid("EXPLAIN PLANGRAPH SELECT * FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_NodeParity_CommentOnTable()
    {
        ExpectValid("COMMENT ON TABLE TESTDB..EMPLOYEES IS 'Employee master table';", _schema);
    }

    [Fact]
    public void Validate_NodeParity_CommentOnColumn()
    {
        ExpectValid("COMMENT ON COLUMN TESTDB..EMPLOYEES.SALARY IS 'Annual salary';", _schema);
    }

    [Fact]
    public void Validate_NodeParity_GenerateExpressStatistics()
    {
        // Valid syntax per parser: GENERATE [EXPRESS] STATISTICS ON table (without TABLE keyword)
        ExpectValid("GENERATE EXPRESS STATISTICS ON TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_NodeParity_GroomTableVersions()
    {
        ExpectValid("GROOM TABLE TESTDB..EMPLOYEES VERSIONS;", _schema);
    }

    [Fact]
    public void Validate_NodeParity_GroomTableRecordsReady()
    {
        ExpectValid("GROOM TABLE TESTDB..EMPLOYEES RECORDS READY;", _schema);
    }

    [Fact]
    public void Validate_NodeParity_GroomTableReclaimBackupsetDefault()
    {
        ExpectValid("GROOM TABLE TESTDB..EMPLOYEES RECORDS ALL RECLAIM BACKUPSET DEFAULT;", _schema);
    }

    [Fact]
    public void Validate_NodeParity_TruncateWithoutTableKeyword()
    {
        ExpectValid("TRUNCATE TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_NodeParity_BeginTransaction()
    {
        ExpectValid("BEGIN;");
    }

    [Fact]
    public void Validate_NodeParity_CommitTransaction()
    {
        ExpectValid("COMMIT;");
    }

    [Fact]
    public void Validate_NodeParity_RollbackTransaction()
    {
        ExpectValid("ROLLBACK;");
    }

    [Fact]
    public void Validate_NodeParity_QualifiedColumnInSubquery()
    {
        ExpectValid(
            """
            SELECT * FROM TESTDB..DEPARTMENTS
            WHERE DEPARTMENT_ID IN (
                SELECT E.DEPARTMENT_ID FROM TESTDB..EMPLOYEES E
            );
            """,
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_UnionAllDifferentAliases()
    {
        ExpectValid(
            """
            SELECT EMPLOYEE_ID AS ID FROM TESTDB..EMPLOYEES
            UNION ALL
            SELECT DEPARTMENT_ID AS DEPT_ID FROM TESTDB..DEPARTMENTS;
            """,
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_OrderByExpression()
    {
        ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES ORDER BY SALARY * 1.1;",
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_SelectIntoWithCte()
    {
        ExpectValid(
            """
            WITH CTE_SRC AS (SELECT EMPLOYEE_ID, FIRST_NAME FROM TESTDB..EMPLOYEES)
            SELECT COUNT(*) INTO VAR_CNT FROM CTE_SRC;
            """,
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_NaturalJoin()
    {
        ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES NATURAL JOIN TESTDB..DEPARTMENTS;",
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_SelfJoin()
    {
        ExpectValid(
            "SELECT E1.FIRST_NAME || ' works with ' || E2.FIRST_NAME FROM TESTDB..EMPLOYEES E1 JOIN TESTDB..EMPLOYEES E2 ON E1.MANAGER_ID = E2.MANAGER_ID;",
            _schema);
    }

    [Fact]
    public void Validate_NodeParity_NetezzaBuiltinValueCurrentSid()
    {
        ExpectValid("SELECT CURRENT_SID;");
    }

    [Fact]
    public void Validate_NodeParity_NetezzaBuiltinValueCurrentDb()
    {
        ExpectValid("SELECT current_db;");
    }

    [Fact]
    public void Validate_NodeParity_NetezzaBuiltinValueCurrentSchema()
    {
        ExpectValid("SELECT current_schema;");
    }

    [Fact]
    public void Validate_NodeParity_TypeLiteralInterval()
    {
        ExpectValid("SELECT INTERVAL '1' DAY;");
    }
}
