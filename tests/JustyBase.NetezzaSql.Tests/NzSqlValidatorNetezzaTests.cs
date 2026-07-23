using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;
using JustyBase.NetezzaSqlParser.Visitor;
using static JustyBase.Tests.NetezzaSqlParser.SqlTestHelpers;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class NzSqlValidatorNetezzaTests
{
    private readonly ISchemaProvider _schema;

    public NzSqlValidatorNetezzaTests()
    {
        _schema = CreateStandardMockSchema();
    }

    // ========================================================================
    // ILIKE — case-insensitive pattern matching
    // ========================================================================

    [Fact]
    public void Validate_Ilike_Simple()
    {
        ExpectValid("SELECT * FROM EMPLOYEES WHERE FIRST_NAME ILIKE '%john%';");
    }

    [Fact]
    public void Validate_Ilike_NotIlike()
    {
        ExpectValid("SELECT * FROM EMPLOYEES WHERE FIRST_NAME NOT ILIKE '%john%';");
    }

    [Fact]
    public void Validate_Ilike_ColumnComparison()
    {
        ExpectValid("SELECT * FROM EMPLOYEES WHERE FIRST_NAME ILIKE LAST_NAME;");
    }

    [Fact]
    public void Validate_Ilike_ComplexWhere()
    {
        ExpectValid("SELECT * FROM EMPLOYEES WHERE FIRST_NAME ILIKE '%a%' AND SALARY > 1000;");
    }

    // ========================================================================
    // FETCH FIRST — row limiting
    // ========================================================================

    [Fact]
    public void Validate_FetchFirst_NRowsOnly()
    {
        ExpectValid("SELECT * FROM EMPLOYEES FETCH FIRST 10 ROWS ONLY;");
    }

    [Fact]
    public void Validate_FetchFirst_SingleRowOnly()
    {
        ExpectValid("SELECT * FROM EMPLOYEES FETCH FIRST 1 ROW ONLY;");
    }

    [Fact]
    public void Validate_FetchFirst_WithoutCountDefaultsTo1()
    {
        ExpectValid("SELECT * FROM EMPLOYEES FETCH FIRST ROW ONLY;");
    }

    [Fact]
    public void Validate_FetchFirst_AfterOrderBy()
    {
        ExpectValid("SELECT * FROM EMPLOYEES ORDER BY SALARY DESC FETCH FIRST 5 ROWS ONLY;");
    }

    [Fact]
    public void Validate_FetchFirst_AfterLimit()
    {
        ExpectValid("SELECT * FROM EMPLOYEES LIMIT 100 FETCH FIRST 10 ROWS ONLY;");
    }

    [Fact]
    public void Validate_FetchFirst_WithOffset()
    {
        ExpectValid("SELECT * FROM EMPLOYEES ORDER BY SALARY LIMIT 100 OFFSET 10 FETCH FIRST 5 ROWS ONLY;");
    }

    // ========================================================================
    // Netezza special built-in values (CURRENT_TIMESTAMP, CURRENT_USER, etc.)
    // ========================================================================

    [Fact]
    public void Validate_BuiltinValues_CurrentTimestamp()
    {
        ExpectValid("SELECT CURRENT_TIMESTAMP FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_BuiltinValues_CurrentDate()
    {
        ExpectValid("SELECT CURRENT_DATE FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_BuiltinValues_CurrentTime()
    {
        ExpectValid("SELECT CURRENT_TIME FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_BuiltinValues_CurrentCatalog()
    {
        ExpectValid("SELECT CURRENT_CATALOG FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_BuiltinValues_CurrentUser()
    {
        ExpectValid("SELECT CURRENT_USER FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_BuiltinValues_CurrentSid()
    {
        ExpectValid("SELECT CURRENT_SID FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_BuiltinValues_SessionUser()
    {
        ExpectValid("SELECT SESSION_USER FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_BuiltinValues_SystemUser()
    {
        ExpectValid("SELECT SYSTEM_USER FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_BuiltinValues_CurrentDbLowercase()
    {
        ExpectValid("SELECT current_db FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_BuiltinValues_CurrentSchemaLowercase()
    {
        ExpectValid("SELECT current_schema FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_BuiltinValues_MultipleSpecialBuiltins()
    {
        ExpectValid("SELECT CURRENT_TIMESTAMP, CURRENT_DATE, CURRENT_USER FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_BuiltinValues_WithAlias()
    {
        ExpectValid("SELECT CURRENT_TIMESTAMP AS TS, CURRENT_DATE AS DT FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_BuiltinValues_InExpressions()
    {
        ExpectValid("SELECT CURRENT_TIMESTAMP + INTERVAL '1' DAY FROM TESTDB..EMPLOYEES;", _schema);
    }

    // ========================================================================
    // Netezza system pseudo-columns (ROWID, CREATEXID, DELETEXID, DATASLICEID)
    // ========================================================================

    [Fact]
    public void Validate_PseudoColumns_RowidAsValidColumn()
    {
        ExpectValid("SELECT ROWID FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_PseudoColumns_CreatexidAsValidColumn()
    {
        ExpectValid("SELECT CREATEXID FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_PseudoColumns_DeletexidAsValidColumn()
    {
        ExpectValid("SELECT DELETEXID FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_PseudoColumns_DatasliceidAsValidColumn()
    {
        ExpectValid("SELECT DATASLICEID FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_PseudoColumns_LowercaseSystemColumns()
    {
        ExpectValid("SELECT rowid, createxid, deletexid, datasliceid FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_PseudoColumns_WithTableAlias()
    {
        ExpectValid("SELECT E.ROWID, E.CREATEXID FROM TESTDB..EMPLOYEES E;", _schema);
    }

    [Fact]
    public void Validate_PseudoColumns_WithQualifiedTable()
    {
        ExpectValid("SELECT ROWID FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_PseudoColumns_InWhereClause()
    {
        ExpectValid("SELECT * FROM TESTDB..EMPLOYEES WHERE ROWID > 100;", _schema);
    }

    [Fact]
    public void Validate_PseudoColumns_InGroupBy()
    {
        ExpectValid("SELECT DATASLICEID, COUNT(*) FROM TESTDB..EMPLOYEES GROUP BY DATASLICEID;", _schema);
    }

    [Fact]
    public void Validate_PseudoColumns_InOrderBy()
    {
        ExpectValid("SELECT * FROM TESTDB..EMPLOYEES ORDER BY CREATEXID;", _schema);
    }

    [Fact]
    public void Validate_PseudoColumns_InJoinConditions()
    {
        ExpectValid(
            """
            SELECT A.ROWID, B.ROWID
            FROM TESTDB..EMPLOYEES A
            JOIN TESTDB..DEPARTMENTS B ON A.CREATEXID = B.CREATEXID;
            """,
            _schema);
    }

    [Fact]
    public void Validate_PseudoColumns_AllSystemColumnsTogether()
    {
        ExpectValid("SELECT ROWID, CREATEXID, DELETEXID, DATASLICEID FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_PseudoColumns_WithAliases()
    {
        ExpectValid("SELECT ROWID AS R, CREATEXID AS CX FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_PseudoColumns_InExpressions()
    {
        ExpectValid("SELECT ROWID + 1, DATASLICEID * 10 FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_PseudoColumns_FromMultipleTablesInJoin()
    {
        ExpectValid(
            """
            SELECT E.ROWID AS EMP_ROWID, D.ROWID AS DEPT_ROWID
            FROM TESTDB..EMPLOYEES E
            JOIN TESTDB..DEPARTMENTS D ON E.DEPARTMENT_ID = D.DEPARTMENT_ID;
            """,
            _schema);
    }

    // ========================================================================
    // Netezza SQL alias reuse — SELECT list
    // ========================================================================

    [Fact]
    public void Validate_AliasReuse_SelectList_EarlierAliasInLaterItem()
    {
        ExpectValid("SELECT 1 AS COL1, COL1 + 1 AS COL2 FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_AliasReuse_SelectList_MultipleAliasReferences()
    {
        ExpectValid(
            """
            SELECT
              1 AS FIRST_COL,
              FIRST_COL + 1 AS SECOND_COL,
              FIRST_COL + SECOND_COL AS THIRD_COL
            FROM TESTDB..EMPLOYEES;
            """,
            _schema);
    }

    [Fact]
    public void Validate_AliasReuse_SelectList_AliasWithArithmetic()
    {
        ExpectValid(
            """
            SELECT
              SALARY * 0.1 AS BONUS,
              SALARY + BONUS AS TOTAL
            FROM TESTDB..EMPLOYEES;
            """,
            _schema);
    }

    [Fact]
    public void Validate_AliasReuse_SelectList_AliasInFunctionCall()
    {
        ExpectValid(
            """
            SELECT
              FIRST_NAME || ' ' || LAST_NAME AS FULL_NAME,
              UPPER(FULL_NAME) AS UPPER_NAME
            FROM TESTDB..EMPLOYEES;
            """,
            _schema);
    }

    [Fact]
    public void Validate_AliasReuse_SelectList_AliasInCaseExpression()
    {
        ExpectValid(
            """
            SELECT
              SALARY AS BASE_SALARY,
              CASE
                WHEN BASE_SALARY > 5000 THEN 'High'
                ELSE 'Low'
              END AS SALARY_CATEGORY
            FROM TESTDB..EMPLOYEES;
            """,
            _schema);
    }

    [Fact]
    public void Validate_AliasReuse_SelectList_ForwardReferenceShouldError()
    {
        ExpectErrorCode(
            "SELECT NONEXISTENT_COL + 1 AS COL1, EMPLOYEE_ID AS NONEXISTENT_COL FROM TESTDB..EMPLOYEES;",
            "SQL004",
            _schema);
    }

    [Fact]
    public void Validate_AliasReuse_SelectList_MixedCaseAliases()
    {
        ExpectValid("SELECT 1 AS MyCol, MyCol + 1 AS NextCol FROM TESTDB..EMPLOYEES;", _schema);
    }

    [Fact]
    public void Validate_AliasReuse_SelectList_NationalCharactersInAlias()
    {
        ExpectValid("SELECT 1 AS ĄĘŚĆĘŃÓŁŻŹ;");
    }

    [Fact]
    public void Validate_AliasReuse_SelectList_QuotedAliasWithQuotedRef()
    {
        ExpectValid(
            """
            SELECT
              E.SALARY AS "BASE_SAL",
              "BASE_SAL" + 1 AS NEXT_SAL
            FROM TESTDB..EMPLOYEES E;
            """,
            _schema);
    }

    [Fact]
    public void Validate_AliasReuse_SelectList_QuotedAliasWithoutQuotesRef()
    {
        ExpectValid(
            """
            SELECT
              E.SALARY AS "BASE_SAL",
              BASE_SAL + 1 AS NEXT_SAL
            FROM TESTDB..EMPLOYEES E;
            """,
            _schema);
    }

    [Fact]
    public void Validate_AliasReuse_SelectList_AliasWithTableAliasQualifier()
    {
        ExpectValid(
            """
            SELECT
              E.SALARY AS BASE_SAL,
              BASE_SAL * 1.1 AS RAISED_SAL
            FROM TESTDB..EMPLOYEES E;
            """,
            _schema);
    }

    [Fact]
    public void Validate_AliasReuse_SelectList_ComplexExpressionChain()
    {
        ExpectValid(
            """
            SELECT
              1 AS A,
              A + 1 AS B,
              B + 1 AS C,
              C + 1 AS D,
              D + 1 AS E
            FROM TESTDB..EMPLOYEES;
            """,
            _schema);
    }

    // ========================================================================
    // Netezza SQL alias reuse — WHERE clause
    // ========================================================================

    [Fact]
    public void Validate_AliasReuse_Where_AliasReference()
    {
        ExpectValid(
            """
            SELECT
              SALARY * 0.1 AS BONUS
            FROM TESTDB..EMPLOYEES
            WHERE BONUS > 100;
            """,
            _schema);
    }

    [Fact]
    public void Validate_AliasReuse_Where_WithAndCondition()
    {
        ExpectValid(
            """
            SELECT
              FIRST_NAME || ' ' || LAST_NAME AS FULL_NAME,
              SALARY AS BASE_SALARY
            FROM TESTDB..EMPLOYEES
            WHERE FULL_NAME LIKE 'John%' AND BASE_SALARY > 5000;
            """,
            _schema);
    }

    [Fact]
    public void Validate_AliasReuse_Where_WithOrCondition()
    {
        ExpectValid(
            """
            SELECT
              SALARY * 0.1 AS BONUS
            FROM TESTDB..EMPLOYEES
            WHERE BONUS > 100 OR BONUS < 50;
            """,
            _schema);
    }

    [Fact]
    public void Validate_AliasReuse_Where_WithInClause()
    {
        ExpectValid(
            """
            SELECT
              DEPARTMENT_ID AS DEPT
            FROM TESTDB..EMPLOYEES
            WHERE DEPT IN (1, 2, 3);
            """,
            _schema);
    }

    [Fact]
    public void Validate_AliasReuse_Where_WithBetween()
    {
        ExpectValid(
            """
            SELECT
              SALARY AS BASE_SAL
            FROM TESTDB..EMPLOYEES
            WHERE BASE_SAL BETWEEN 3000 AND 8000;
            """,
            _schema);
    }

    // ========================================================================
    // Netezza SQL alias reuse — GROUP BY clause
    // ========================================================================

    [Fact]
    public void Validate_AliasReuse_GroupBy_AliasReference()
    {
        ExpectValid(
            """
            SELECT
              DEPARTMENT_ID AS DEPT,
              COUNT(*) AS EMP_COUNT
            FROM TESTDB..EMPLOYEES
            GROUP BY DEPT;
            """,
            _schema);
    }

    [Fact]
    public void Validate_AliasReuse_GroupBy_MultipleColumns()
    {
        ExpectValid(
            """
            SELECT
              DEPARTMENT_ID AS DEPT,
              STATUS AS EMP_STATUS,
              COUNT(*) AS EMP_COUNT
            FROM TESTDB..EMPLOYEES
            GROUP BY DEPT, EMP_STATUS;
            """,
            _schema);
    }

    [Fact]
    public void Validate_AliasReuse_GroupBy_WithAggregation()
    {
        ExpectValid(
            """
            SELECT
              CASE
                WHEN SALARY > 5000 THEN 'High'
                ELSE 'Low'
              END AS SALARY_BUCKET,
              COUNT(*) AS EMP_COUNT,
              AVG(SALARY) AS AVG_SAL
            FROM TESTDB..EMPLOYEES
            GROUP BY SALARY_BUCKET;
            """,
            _schema);
    }

    // ========================================================================
    // Netezza SQL alias reuse — HAVING clause
    // ========================================================================

    [Fact]
    public void Validate_AliasReuse_Having_AliasReference()
    {
        ExpectValid(
            """
            SELECT
              DEPARTMENT_ID AS DEPT,
              COUNT(*) AS EMP_COUNT
            FROM TESTDB..EMPLOYEES
            GROUP BY DEPT
            HAVING EMP_COUNT > 5;
            """,
            _schema);
    }

    [Fact]
    public void Validate_AliasReuse_Having_WithAggregationResult()
    {
        ExpectValid(
            """
            SELECT
              DEPARTMENT_ID AS DEPT,
              AVG(SALARY) AS AVG_SAL,
              COUNT(*) AS EMP_COUNT
            FROM TESTDB..EMPLOYEES
            GROUP BY DEPT
            HAVING AVG_SAL > 5000 AND EMP_COUNT > 10;
            """,
            _schema);
    }

    // ========================================================================
    // Netezza SQL alias reuse — ORDER BY clause
    // ========================================================================

    [Fact]
    public void Validate_AliasReuse_OrderBy_AliasReference()
    {
        ExpectValid(
            """
            SELECT
              FIRST_NAME || ' ' || LAST_NAME AS FULL_NAME
            FROM TESTDB..EMPLOYEES
            ORDER BY FULL_NAME;
            """,
            _schema);
    }

    [Fact]
    public void Validate_AliasReuse_OrderBy_WithDesc()
    {
        ExpectValid(
            """
            SELECT
              SALARY * 0.1 AS BONUS
            FROM TESTDB..EMPLOYEES
            ORDER BY BONUS DESC;
            """,
            _schema);
    }

    [Fact]
    public void Validate_AliasReuse_OrderBy_MultipleAliases()
    {
        ExpectValid(
            """
            SELECT
              DEPARTMENT_ID AS DEPT,
              SALARY AS BASE_SALARY
            FROM TESTDB..EMPLOYEES
            ORDER BY DEPT ASC, BASE_SALARY DESC;
            """,
            _schema);
    }

    [Fact]
    public void Validate_AliasReuse_OrderBy_WithNullsFirst()
    {
        ExpectValid(
            """
            SELECT
              MANAGER_ID AS MGR
            FROM TESTDB..EMPLOYEES
            ORDER BY MGR NULLS FIRST;
            """,
            _schema);
    }
}
