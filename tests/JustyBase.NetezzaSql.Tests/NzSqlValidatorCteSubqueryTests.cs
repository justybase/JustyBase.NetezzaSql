using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class NzSqlValidatorCteSubqueryTests
{
    private readonly ISchemaProvider _schema;

    public NzSqlValidatorCteSubqueryTests()
    {
        _schema = SqlTestHelpers.CreateStandardMockSchema();
    }

    // ========================================================================
    // CTE — valid syntax
    // ========================================================================

    [Fact]
    public void Validate_Cte_Simple()
    {
        SqlTestHelpers.ExpectValid("WITH CTE AS (SELECT 1 AS VAL) SELECT VAL FROM CTE;");
    }

    [Fact]
    public void Validate_Cte_Multiple()
    {
        SqlTestHelpers.ExpectValid(
            """
            WITH
                CTE_A AS (SELECT 1 AS A),
                CTE_B AS (SELECT 2 AS B)
            SELECT CTE_A.A, CTE_B.B FROM CTE_A CROSS JOIN CTE_B;
            """);
    }

    [Fact]
    public void Validate_Cte_WithRealTable()
    {
        SqlTestHelpers.ExpectValid(
            """
            WITH EMP_CTE AS (
                SELECT E.EMPLOYEE_ID, E.FIRST_NAME, E.SALARY
                FROM TESTDB..EMPLOYEES E
                WHERE E.SALARY > 3000
            )
            SELECT C.EMPLOYEE_ID, C.FIRST_NAME
            FROM EMP_CTE C
            ORDER BY C.SALARY DESC;
            """,
            _schema);
    }

    [Fact]
    public void Validate_Cte_WithJoin()
    {
        SqlTestHelpers.ExpectValid(
            """
            WITH DEPT_CTE AS (
                SELECT DEPARTMENT_ID, DEPARTMENT_NAME FROM TESTDB..DEPARTMENTS
            )
            SELECT E.FIRST_NAME, D.DEPARTMENT_NAME
            FROM TESTDB..EMPLOYEES E
            JOIN DEPT_CTE D ON E.DEPARTMENT_ID = D.DEPARTMENT_ID;
            """,
            _schema);
    }

    [Fact]
    public void Validate_Cte_Chained()
    {
        SqlTestHelpers.ExpectValid(
            """
            WITH
                CTE_1 AS (SELECT 1 AS X),
                CTE_2 AS (SELECT X + 1 AS Y FROM CTE_1)
            SELECT Y FROM CTE_2;
            """);
    }

    [Fact]
    public void Validate_Cte_WithSubqueryInWhere()
    {
        SqlTestHelpers.ExpectValid(
            """
            WITH ACTIVE_DEPTS AS (
                SELECT D.DEPARTMENT_ID FROM TESTDB..DEPARTMENTS D
            )
            SELECT E.* FROM TESTDB..EMPLOYEES E
            WHERE E.DEPARTMENT_ID IN (SELECT AD.DEPARTMENT_ID FROM ACTIVE_DEPTS AD);
            """,
            _schema);
    }

    // ========================================================================
    // CTE — WITH RECURSIVE
    // ========================================================================

    [Fact]
    public void Validate_Cte_Recursive_Simple()
    {
        SqlTestHelpers.ExpectValid(
            """
            WITH RECURSIVE CTE AS (
                SELECT 1 AS N
                UNION ALL
                SELECT N + 1 FROM CTE WHERE N < 10
            )
            SELECT N FROM CTE;
            """);
    }

    [Fact]
    public void Validate_Cte_Recursive_WithTable()
    {
        SqlTestHelpers.ExpectValid(
            """
            WITH RECURSIVE EMP_HIERARCHY AS (
                SELECT EMPLOYEE_ID, MANAGER_ID, FIRST_NAME, 1 AS LEVEL
                FROM TESTDB..EMPLOYEES
                WHERE MANAGER_ID IS NULL
                UNION ALL
                SELECT E.EMPLOYEE_ID, E.MANAGER_ID, E.FIRST_NAME, EH.LEVEL + 1
                FROM TESTDB..EMPLOYEES E
                JOIN EMP_HIERARCHY EH ON E.MANAGER_ID = EH.EMPLOYEE_ID
            )
            SELECT * FROM EMP_HIERARCHY;
            """,
            _schema);
    }

    [Fact]
    public void Validate_Cte_Recursive_MultipleColumns()
    {
        SqlTestHelpers.ExpectValid(
            """
            WITH RECURSIVE NUMBERS AS (
                SELECT 1 AS ID, 'Start' AS LABEL
                UNION ALL
                SELECT ID + 1, 'Next'
                FROM NUMBERS
                WHERE ID < 5
            )
            SELECT * FROM NUMBERS;
            """);
    }

    [Fact]
    public void Validate_Cte_Recursive_Error_NonexistentColumn()
    {
        SqlTestHelpers.ExpectErrorCode(
            """
            WITH RECURSIVE CTE AS (
                SELECT EMPLOYEE_ID, NONEXISTENT_COL FROM TESTDB..EMPLOYEES
                UNION ALL
                SELECT EMPLOYEE_ID, NONEXISTENT_COL FROM CTE WHERE EMPLOYEE_ID < 100
            )
            SELECT * FROM CTE;
            """,
            "SQL004",
            _schema);
    }

    // ========================================================================
    // CTE — name shadowing
    // ========================================================================

    [Fact]
    public void Validate_Cte_Shadowing_TableName()
    {
        SqlTestHelpers.ExpectValid("WITH EMPLOYEES AS (SELECT 1 AS ID) SELECT ID FROM EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_Cte_Chained_WithSameColumnName()
    {
        SqlTestHelpers.ExpectValid(
            """
            WITH
                CTE1 AS (SELECT 1 AS COL_A),
                CTE2 AS (SELECT COL_A + 1 AS COL_B FROM CTE1)
            SELECT COL_B FROM CTE2;
            """);
    }

    [Fact]
    public void Validate_Cte_AmbiguousColumn()
    {
        SqlTestHelpers.ExpectErrorCode(
            """
            WITH EMP AS (SELECT 1 AS EMPLOYEE_ID)
            SELECT EMPLOYEE_ID FROM EMP, TESTDB..EMPLOYEES E WHERE EMP.EMPLOYEE_ID = E.EMPLOYEE_ID;
            """,
            "SQL008",
            _schema);
    }

    [Fact]
    public void Validate_Cte_NestedScope_InnerReferencesOuter()
    {
        SqlTestHelpers.ExpectValid(
            """
            WITH OUTER_CTE AS (SELECT 1 AS X)
            SELECT * FROM (
                SELECT X FROM OUTER_CTE
            ) AS INNER_Q;
            """);
    }

    // ========================================================================
    // CTE — nested WITH (Netezza extension)
    // ========================================================================

    [Fact]
    public void Validate_Cte_NestedWith_InBody()
    {
        SqlTestHelpers.ExpectValid(
            """
            WITH ABC AS
            (
                WITH DEF AS (
                    SELECT 1 AS ONE FROM TESTDB..EMPLOYEES
                )
                SELECT 9 AS NINE
            )
            SELECT * FROM ABC;
            """,
            _schema);
    }

    [Fact]
    public void Validate_Cte_NestedWith_MultipleInner()
    {
        SqlTestHelpers.ExpectValid(
            """
            WITH ABC AS
            (
                WITH DEF AS (
                    SELECT 1 AS ONE FROM TESTDB..EMPLOYEES
                )
                , EFG AS (
                    SELECT 2 AS TWO FROM TESTDB..EMPLOYEES
                )
                SELECT 9 AS NINE
            )
            SELECT * FROM ABC;
            """,
            _schema);
    }

    [Fact]
    public void Validate_Cte_AsAll()
    {
        SqlTestHelpers.ExpectValid(
            """
            WITH ABC AS ALL (
                SELECT 1 AS ONE FROM TESTDB..EMPLOYEES
            )
            SELECT * FROM ABC;
            """,
            _schema);
    }

    [Fact]
    public void Validate_Cte_AsAll_WithNested()
    {
        SqlTestHelpers.ExpectValid(
            """
            WITH ABC AS
            (
                WITH DEF AS (
                    SELECT 1 AS ONE FROM TESTDB..EMPLOYEES
                )
                , EFG AS ALL
                (
                    SELECT 2 AS TWO FROM TESTDB..EMPLOYEES
                )
                SELECT 9 AS NINE
            )
            SELECT * FROM ABC;
            """,
            _schema);
    }

    [Fact]
    public void Validate_Cte_DeeplyNested()
    {
        SqlTestHelpers.ExpectValid(
            """
            WITH OUTER_CTE AS (
                WITH INNER_CTE AS (
                    WITH DEEPEST_CTE AS (
                        SELECT 1 AS VAL
                    )
                    SELECT VAL FROM DEEPEST_CTE
                )
                SELECT VAL FROM INNER_CTE
            )
            SELECT * FROM OUTER_CTE;
            """);
    }

    [Fact]
    public void Validate_Cte_NestedWith_ColumnList()
    {
        SqlTestHelpers.ExpectValid(
            """
            WITH ABC (COL1) AS (
                WITH DEF AS (SELECT 1 AS ONE)
                SELECT ONE FROM DEF
            )
            SELECT COL1 FROM ABC;
            """);
    }

    [Fact]
    public void Validate_Cte_MultipleOuter_WithNested()
    {
        SqlTestHelpers.ExpectValid(
            """
            WITH
                CTE_A AS (SELECT 1 AS A),
                CTE_B AS (
                    WITH INNER_CTE AS (SELECT 2 AS B)
                    SELECT B FROM INNER_CTE
                )
            SELECT CTE_A.A, CTE_B.B FROM CTE_A CROSS JOIN CTE_B;
            """);
    }

    [Fact]
    public void Validate_Cte_NestedWith_UnionAllInsideCte()
    {
        SqlTestHelpers.ExpectValid(
            """
            WITH ABC AS (
                WITH DEF AS (
                    SELECT 1 AS X
                    UNION ALL
                    SELECT 2 AS X
                )
                SELECT X FROM DEF
            )
            SELECT * FROM ABC;
            """);
    }

    // ========================================================================
    // CTE — syntax errors
    // ========================================================================

    [Fact]
    public void Validate_Cte_SyntaxError_MissingParentheses()
    {
        SqlTestHelpers.ExpectSyntaxError("WITH CTE AS SELECT 1 AS VAL SELECT * FROM CTE;");
    }

    [Fact]
    public void Validate_Cte_SyntaxError_MissingFinalSelect()
    {
        SqlTestHelpers.ExpectSyntaxError("WITH CTE AS (SELECT 1 AS VAL);");
    }

    [Fact]
    public void Validate_Cte_SyntaxError_EmptyBody()
    {
        SqlTestHelpers.ExpectSyntaxError("WITH CTE AS () SELECT 1;");
    }

    [Fact]
    public void Validate_Cte_SyntaxError_TrailingComma()
    {
        SqlTestHelpers.ExpectSyntaxError("WITH CTE AS (SELECT 1 AS VAL), SELECT * FROM CTE;");
    }

    [Fact]
    public void Validate_Cte_SyntaxError_MissingName()
    {
        SqlTestHelpers.ExpectSyntaxError("WITH AS (SELECT 1 AS VAL) SELECT 1;");
    }

    // ========================================================================
    // Subqueries — valid syntax
    // ========================================================================

    [Fact]
    public void Validate_Subquery_ScalarInSelectList()
    {
        SqlTestHelpers.ExpectValid(
            "SELECT E.FIRST_NAME, (SELECT COUNT(*) FROM TESTDB..ORDERS) AS ORDER_CNT FROM TESTDB..EMPLOYEES E;",
            _schema);
    }

    [Fact]
    public void Validate_Subquery_CorrelatedInWhere()
    {
        SqlTestHelpers.ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES E WHERE E.SALARY > (SELECT AVG(E2.SALARY) FROM TESTDB..EMPLOYEES E2 WHERE E2.DEPARTMENT_ID = E.DEPARTMENT_ID);",
            _schema);
    }

    [Fact]
    public void Validate_Subquery_FromTableSource()
    {
        SqlTestHelpers.ExpectValid(
            "SELECT SUB.EMPLOYEE_ID FROM (SELECT EMPLOYEE_ID, SALARY FROM TESTDB..EMPLOYEES WHERE SALARY > 1000) SUB;",
            _schema);
    }

    [Fact]
    public void Validate_Subquery_DeeplyNested()
    {
        SqlTestHelpers.ExpectValid(
            "SELECT * FROM (SELECT * FROM (SELECT 1 AS X) INNER_Q) OUTER_Q;");
    }

    // ========================================================================
    // Subqueries — syntax errors
    // ========================================================================

    [Fact]
    public void Validate_Subquery_SyntaxError_MissingClosingParen()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT * FROM (SELECT 1 AS X;");
    }

    [Fact]
    public void Validate_Subquery_SyntaxError_MissingOpeningParen()
    {
        SqlTestHelpers.ExpectSyntaxError("SELECT * FROM SELECT 1 AS X) S;");
    }
}
