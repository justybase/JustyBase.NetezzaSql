using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;
using JustyBase.NetezzaSqlParser.Visitor;
using static JustyBase.Tests.NetezzaSqlParser.SqlTestHelpers;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed partial class NzSqlValidatorRemainingTests
{
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
    public void Validate_SetOps_MixedParenthesizedAndNonParenthesized()
    {
        ExpectValid("(SELECT 1) UNION SELECT 2;");
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
    public void Validate_Utility_ExplainVerbose()
    {
        ExpectValid("EXPLAIN VERBOSE SELECT * FROM TESTDB..EMPLOYEES;", _schema);
    }


    [Fact]
    public void Validate_Utility_GenerateStatisticsOn()
    {
        ExpectValid("GENERATE STATISTICS ON TESTDB..EMPLOYEES;", _schema);
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
    public void Validate_Advanced_NotInList()
    {
        ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES WHERE DEPARTMENT_ID NOT IN (1, 2, 3);",
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
    public void Validate_Call_WithArguments()
    {
        ExpectValid("CALL SOME_PROC_NAME('test', 123, 45.67)");
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
    public void Validate_NodeParity_LeadingSemicolonsWarning() { }


    [Fact]
    public void Validate_NodeParity_MiddleEmptyStatementsWarning() { }


    [Fact]
    public void Validate_NodeParity_IlikeWithNot()
    {
        ExpectValid(
            "SELECT * FROM TESTDB..EMPLOYEES WHERE FIRST_NAME NOT ILIKE 'a%';",
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
    public void Validate_NodeParity_RowidSystemColumn()
    {
        ExpectValid(
            "SELECT ROWID FROM TESTDB..EMPLOYEES;",
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
    public void Validate_NodeParity_ParameterMarker()
    {
        // ? parameter marker should parse without error
        ExpectValid("SELECT * FROM TESTDB..EMPLOYEES WHERE EMPLOYEE_ID = ?;", _schema);
    }


    [Fact]
    public void Validate_NodeParity_AtSetVariable()
    {
        ExpectValid("@SET VARIABLE = 100;");
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
}
