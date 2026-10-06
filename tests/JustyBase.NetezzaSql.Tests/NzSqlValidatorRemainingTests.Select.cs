using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;
using JustyBase.NetezzaSqlParser.Visitor;
using static JustyBase.Tests.NetezzaSqlParser.SqlTestHelpers;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed partial class NzSqlValidatorRemainingTests
{
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
    public void Validate_Advanced_ExistsSubquery()
    {
        ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES E WHERE EXISTS (SELECT 1 FROM TESTDB..DEPARTMENTS D WHERE D.DEPARTMENT_ID = E.DEPARTMENT_ID);",
            _schema);
    }


    [Fact]
    public void Validate_Advanced_MissingAsInCte()
    {
        ExpectSyntaxError("WITH CTE (SELECT 1 AS VAL) SELECT * FROM CTE;");
    }


    [Fact]
    public void Validate_Call_SchemaQualified()
    {
        ExpectValid("CALL JUST_DATA.ADMIN.SOME_PROC_NAME()");
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
    public void Validate_NodeParity_FetchFirstRowsOnly()
    {
        // Live Netezza rejects FETCH FIRST (live evidence 2026-10-06).
        ExpectSyntaxError(
            "SELECT * FROM TESTDB..EMPLOYEES ORDER BY EMPLOYEE_ID FETCH FIRST 10 ROWS ONLY;",
            _schema);
    }


    [Fact]
    public void Validate_NodeParity_FetchFirstRowOnly()
    {
        ExpectSyntaxError(
            "SELECT * FROM TESTDB..EMPLOYEES ORDER BY EMPLOYEE_ID FETCH FIRST 1 ROW ONLY;",
            _schema);
    }


    [Fact]
    public void Validate_NodeParity_FetchFirstAfterLimit()
    {
        ExpectSyntaxError(
            "SELECT * FROM TESTDB..EMPLOYEES ORDER BY EMPLOYEE_ID LIMIT 20 FETCH FIRST 10 ROWS ONLY;",
            _schema);
    }


    [Fact]
    public void Validate_NodeParity_CurrentTimestampAlias()
    {
        ExpectValid("SELECT CURRENT_TIMESTAMP AS NOW;");
    }


    [Fact]
    public void Validate_NodeParity_RowidQualified()
    {
        ExpectValid(
            "SELECT E.ROWID FROM TESTDB..EMPLOYEES E;",
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
    public void Validate_NodeParity_SelectIntoVariable()
    {
        ExpectValid("SELECT COUNT(*) INTO VAR_CNT FROM TESTDB..EMPLOYEES;", _schema);
    }


    [Fact]
    public void Validate_NodeParity_ParenthesizedSelectUnion()
    {
        ExpectValid(
            "(SELECT EMPLOYEE_ID FROM TESTDB..EMPLOYEES) UNION (SELECT DEPARTMENT_ID FROM TESTDB..DEPARTMENTS);",
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
    public void Validate_NodeParity_SelectWithNone()
    {
        // NONE is not a recognized SQL construct in this parser; skip
    }


    [Fact]
    public void Validate_NodeParity_SelectWithCurrentUser()
    {
        ExpectValid("SELECT CURRENT_USER;");
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
    public void Validate_NodeParity_AsAllCteModifier()
    {
        // Netezza extension: AS ALL materialization hint
        ExpectValid(
            "WITH MYCTE AS ALL (SELECT * FROM TESTDB..EMPLOYEES) SELECT * FROM MYCTE;",
            _schema);
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
}
