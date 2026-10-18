using JustyBase.NetezzaSqlParser.Visitor;
using static JustyBase.Tests.NetezzaSqlParser.SqlTestHelpers;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed partial class NzSqlValidatorDdlTests
{
    [Fact]
    public void Ctas_WithParenthesizedSelect()
    {
        ExpectValid(
            "CREATE TABLE T_NEW AS (SELECT * FROM TESTDB..EMPLOYEES) DISTRIBUTE ON RANDOM;",
            _schema);
    }


    [Fact]
    public void Ctas_WithoutParentheses()
    {
        ExpectValid(
            "CREATE TABLE T_NEW AS SELECT EMPLOYEE_ID, SALARY FROM TESTDB..EMPLOYEES;",
            _schema);
    }


    [Fact]
    public void Ctas_WithLimit()
    {
        ExpectValid(
            "CREATE TABLE SAMPLE_EMP AS (SELECT * FROM TESTDB..EMPLOYEES LIMIT 100) DISTRIBUTE ON RANDOM;",
            _schema);
    }


    [Fact]
    public void Ctas_ComplexSelect()
    {
        ExpectValid(
            "CREATE TABLE TESTDB..SUMMARY AS SELECT DEPARTMENT_ID, COUNT(*) AS CNT, AVG(SALARY) AS AVG_SAL FROM TESTDB..EMPLOYEES GROUP BY DEPARTMENT_ID;",
            _schema);
    }


    [Fact]
    public void Ctas_WithJoin()
    {
        ExpectValid(
            "CREATE TABLE TESTDB..EMP_DEPT AS SELECT E.FIRST_NAME, D.DEPARTMENT_NAME FROM TESTDB..EMPLOYEES E JOIN TESTDB..DEPARTMENTS D ON E.DEPARTMENT_ID = D.DEPARTMENT_ID;",
            _schema);
    }


    [Fact]
    public void Ctas_WithDistributeOn()
    {
        ExpectValid(
            "CREATE TABLE TESTDB..DIST_TABLE AS SELECT * FROM TESTDB..EMPLOYEES DISTRIBUTE ON (EMPLOYEE_ID);",
            _schema);
    }




    [Fact]
    public void Ctas_MissingAsKeyword()
    {
        ExpectSyntaxError("CREATE TABLE T_NEW (SELECT 1 AS COL);", _schema);
    }


    [Fact]
    public void Ctas_MissingSelectAfterAs()
    {
        ExpectSyntaxError("CREATE TABLE T_NEW AS;", _schema);
    }




    [Fact]
    public void ExplainSelect()
    {
        ExpectValid("EXPLAIN SELECT * FROM TESTDB..EMPLOYEES;", _schema);
    }


    [Fact]
    public void ExplainVerboseSelect()
    {
        ExpectValid("EXPLAIN VERBOSE SELECT * FROM TESTDB..EMPLOYEES;", _schema);
    }


    [Fact]
    public void ExplainPlangraphSelect()
    {
        ExpectValid("EXPLAIN PLANGRAPH SELECT * FROM TESTDB..EMPLOYEES;", _schema);
    }


    [Fact]
    public void Explain_WithCtas()
    {
        ExpectValid("EXPLAIN CREATE TABLE TMP_EX AS (SELECT 1 AS C);", _schema);
    }




    [Fact]
    public void GenerateStatistics_Bare()
    {
        ExpectValid("GENERATE STATISTICS;", _schema);
    }


    [Fact]
    public void GenerateStatistics_WithColumnList()
    {
        ExpectValid(
            "GENERATE STATISTICS ON TESTDB..EMPLOYEES (EMPLOYEE_ID, SALARY);",
            _schema);
    }




    [Fact]
    public void Grant_MultiplePrivileges()
    {
        ExpectValid(
            "GRANT SELECT, INSERT, UPDATE, DELETE ON my_schema.my_table TO admin",
            _schema);
    }


    [Fact]
    public void Grant_WithGrantOption()
    {
        ExpectValid("GRANT ALL ON my_table TO admin WITH GRANT OPTION", _schema);
    }


    [Fact]
    public void Grant_ToPublic()
    {
        ExpectValid("GRANT SELECT ON my_table TO PUBLIC", _schema);
    }


    [Fact]
    public void Grant_ToGroup()
    {
        ExpectValid("GRANT SELECT ON my_table TO GROUP dev_team", _schema);
    }


    [Fact]
    public void Grant_AdminPrivileges()
    {
        ExpectValid("GRANT LIST ON SCHEMA my_schema TO admin", _schema);
    }


    [Fact]
    public void Revoke_Simple()
    {
        ExpectValid("REVOKE SELECT ON my_table FROM admin", _schema);
    }


    [Fact]
    public void Revoke_MultiplePrivileges()
    {
        ExpectValid("REVOKE INSERT, DELETE ON my_table FROM admin", _schema);
    }




    [Fact]
    public void DistributeOnHash()
    {
        ExpectValid(
            "CREATE TABLE t1 (id INT, name VARCHAR(50)) DISTRIBUTE ON HASH (id)",
            _schema);
    }


    [Fact]
    public void DistributeOnHash_MultiColumn()
    {
        ExpectValid(
            "CREATE TABLE t1 (id INT, name VARCHAR(50), dept INT) DISTRIBUTE ON HASH (id, dept)",
            _schema);
    }


    [Fact]
    public void DistributeOnRandom()
    {
        ExpectValid("CREATE TABLE t1 (id INT) DISTRIBUTE ON RANDOM", _schema);
    }


    [Fact]
    public void DistributeOn_WithoutHash()
    {
        ExpectValid(
            "CREATE TABLE t1 (id INT, name VARCHAR(50)) DISTRIBUTE ON (id)",
            _schema);
    }


    [Fact]
    public void OrganizeOnNone()
    {
        ExpectValid(
            "CREATE TABLE t1 (id INT, event_date DATE) DISTRIBUTE ON RANDOM ORGANIZE ON NONE",
            _schema);
    }




    [Fact]
    public void CheckConstraint()
    {
        ExpectValid("CREATE TABLE t (id INT, age INT, CHECK (age > 0))", _schema);
    }


    [Fact]
    public void NamedConstraintPrefixOnColumn()
    {
        ExpectValid(
            "CREATE TABLE t (id INT CONSTRAINT pk_t PRIMARY KEY, name VARCHAR(100) NOT NULL)",
            _schema);
    }


    [Fact]
    public void ReferencesColumnConstraint()
    {
        ExpectValid(
            "CREATE TABLE t (id INT, dept_id INT REFERENCES departments)",
            _schema);
    }


    [Fact]
    public void ColumnLevelDefaultWithNotNull()
    {
        ExpectValid(
            "CREATE TABLE t (id INT NOT NULL, name VARCHAR(50) DEFAULT 'N/A' NOT NULL)",
            _schema);
    }




    [Fact]
    public void Truncate_SchemaQualified()
    {
        ExpectValid("TRUNCATE TABLE mydb..my_table", _schema);
    }




    [Fact]
    public void ExplainSelect_Extended()
    {
        ExpectValid("EXPLAIN SELECT * FROM my_table", _schema);
    }


    [Fact]
    public void ExplainPlantextSelect()
    {
        ExpectValid("EXPLAIN PLANTEXT SELECT * FROM my_table", _schema);
    }


    [Fact]
    public void ExplainPlangraphSelect_Extended()
    {
        ExpectValid("EXPLAIN PLANGRAPH SELECT * FROM my_table", _schema);
    }




    [Fact]
    public void GenerateExpressStatistics_WithColumns()
    {
        ExpectValid("GENERATE EXPRESS STATISTICS ON my_table (col1, col2, col3)", _schema);
    }




    [Fact]
    public void Grant_WithoutArguments()
    {
        ExpectValid("GRANT SELECT ON t TO user;", _schema);
    }


    [Fact]
    public void Revoke_WithoutArguments()
    {
        ExpectValid("REVOKE SELECT ON t FROM user;", _schema);
    }




    [Fact]
    public void Select_MissingFromKeyword()
    {
        ExpectSyntaxError("SELECT EMPLOYEE_ID TESTDB..EMPLOYEES;", _schema);
    }


    [Fact]
    public void GroupBy_WithoutColumnList()
    {
        ExpectSyntaxError("SELECT COUNT(*) FROM t GROUP BY", _schema);
    }


    [Fact]
    public void Having_WithoutExpression()
    {
        ExpectSyntaxError("SELECT COUNT(*) FROM t GROUP BY id HAVING", _schema);
    }


    [Fact]
    public void OrderBy_TrailingComma()
    {
        ExpectSyntaxError("SELECT id FROM t ORDER BY id,", _schema);
    }


    [Fact]
    public void Limit_WithoutNumber()
    {
        ExpectSyntaxError("SELECT * FROM t LIMIT", _schema);
    }


    [Fact]
    public void Select_DoubleDistinct()
    {
        ExpectSyntaxError("SELECT DISTINCT DISTINCT id FROM t", _schema);
    }




    [Fact]
    public void ShowSchema()
    {
        ExpectValid("SHOW SCHEMA;", _schema);
    }


    [Fact]
    public void ShowSession()
    {
        ExpectValid("SHOW SESSION;", _schema);
    }


    [Fact]
    public void CopyCommand()
    {
        ExpectValid("COPY TESTDB..EMPLOYEES TO '/tmp/employees.csv';", _schema);
    }


    [Fact]
    public void MergeCommand()
    {
        ExpectValid(
            "MERGE INTO TESTDB..EMPLOYEES E USING TESTDB..DEPARTMENTS D ON E.DEPARTMENT_ID = D.DEPARTMENT_ID WHEN MATCHED THEN UPDATE SET STATUS = 'A';",
            _schema);
    }


    [Fact]
    public void ReindexDatabaseCommand()
    {
        ExpectValid("REINDEX DATABASE TESTDB;", _schema);
    }


    [Fact]
    public void ResetSessionCommand()
    {
        ExpectValid("RESET SESSION;", _schema);
    }


    [Fact]
    public void BeginTransactionCommand()
    {
        ExpectValid("BEGIN;", _schema);
    }




    [Fact]
    public void CallStatement()
    {
        ExpectValid("CALL SOME_PROC_NAME()", _schema);
    }


    [Fact]
    public void CallStatement_SchemaQualified()
    {
        ExpectValid("CALL JUST_DATA.ADMIN.SOME_PROC_NAME()", _schema);
    }


    [Fact]
    public void CallStatement_WithArguments()
    {
        ExpectValid("CALL SOME_PROC_NAME('test', 123, 45.67)", _schema);
    }


    [Fact]
    public void ExecuteProcedure()
    {
        ExpectValid("EXECUTE PROCEDURE SOME_PROC_NAME()", _schema);
    }


    [Fact]
    public void Execute_WithoutProcedureKeyword()
    {
        ExpectValid("EXECUTE SOME_PROC_NAME()", _schema);
    }


    [Fact]
    public void ExecShorthand()
    {
        ExpectValid("EXEC SOME_PROC_NAME()", _schema);
    }


    [Fact]
    public void ExecProcedureShorthand()
    {
        ExpectValid("EXEC PROCEDURE SOME_PROC_NAME()", _schema);
    }




    [Fact]
    public void CommitStatement()
    {
        ExpectValid("COMMIT;", _schema);
    }


    [Fact]
    public void RollbackStatement()
    {
        ExpectValid("ROLLBACK;", _schema);
    }




    [Fact]
    public void VariableSetStatement()
    {
        ExpectValid("@SET MY_VAR = 10;");
    }


    [Fact]
    public void SetCatalogStatement()
    {
        ExpectValid("SET CATALOG JUST_DATA;", _schema);
    }


    [Fact]
    public void VariableUsageDollar()
    {
        ExpectValid("SELECT $MY_VAR FROM TESTDB..EMPLOYEES;");
    }


    [Fact]
    public void VariableUsageDollarBrace()
    {
        ExpectValid("SELECT ${MY_VAR} FROM TESTDB..EMPLOYEES;");
    }

    // Live Netezza 11.2 rejects WITH ... INSERT: only SELECT or ( may follow
    // the WITH clause (evidence-campaign 20261018f, dotnet-template.026).
    // INSERT ... WITH ... (CTE after INSERT, in query position) stays valid,
    // as does a WITH-led query inside CTAS.
    [Theory]
    [InlineData("WITH SRC AS (SELECT 1 AS CODE, 'Test' AS TITLE)\nINSERT INTO TESTDB..FILMS (CODE, TITLE) SELECT CODE, TITLE FROM SRC;")]
    [InlineData("WITH SRC AS (SELECT 1 AS CODE) INSERT INTO T (CODE) SELECT CODE FROM SRC;")]
    public void WithClauseFollowedByInsertIsRejected(string sql)
    {
        ExpectSyntaxError(sql, _schema);
    }

    [Fact]
    public void WithInsertRejectionExplainsAllowedFollowers()
    {
        var result = Validate(
            "WITH SRC AS (SELECT 1 AS CODE) INSERT INTO T (CODE) SELECT CODE FROM SRC;",
            _schema);
        var error = Assert.Single(result.Errors, e => e.Code.StartsWith("PAR") || e.Code.StartsWith("LEX"));
        Assert.Contains("SELECT", error.Message, StringComparison.Ordinal);
        Assert.Contains("INSERT", error.Message, StringComparison.Ordinal);
    }

    // Live Netezza 11.2 rejects INSERT ... WITH name (SELECT ...) without AS
    // (dotnet-template.017); WITH name AS (...) stays valid.
    [Theory]
    [InlineData("INSERT INTO TESTDB..FILMS\nWITH ABC (SELECT 1 AS CODE, 'Test' AS TITLE)\nSELECT CODE, TITLE FROM ABC;")]
    public void InsertWithWithoutAsIsRejected(string sql)
    {
        ExpectSyntaxError(sql, _schema);
    }

    [Theory]
    [InlineData("WITH SRC AS (SELECT 1 AS CODE) SELECT CODE FROM SRC;")]
    [InlineData("WITH SRC AS (SELECT 1 AS CODE) (SELECT CODE FROM SRC);")]
    [InlineData("INSERT INTO TESTDB..FILMS (CODE, TITLE) SELECT CODE, TITLE FROM TESTDB..FILMS;")]
    [InlineData("INSERT INTO TESTDB..FILMS (CODE) WITH SRC AS (SELECT 1 AS CODE) SELECT CODE FROM SRC;")]
    [InlineData("CREATE TEMP TABLE T AS WITH SRC AS (SELECT 1 AS CODE) SELECT CODE FROM SRC;")]
    public void WithSelectInsertWithAndCtasWithRemainValid(string sql)
    {
        ExpectValid(sql, _schema);
    }

    // Live Netezza 11.2 rejects SERIAL/BIGSERIAL as column typenames
    // (evidence-campaign 20261018f, dotnet-template.111).
    [Theory]
    [InlineData("CREATE TABLE SERIAL_TYPES (ID SERIAL, BIG_ID BIGSERIAL);")]
    [InlineData("CREATE TABLE T1 (ID SERIAL);")]
    [InlineData("CREATE TABLE T1 (ID BIGSERIAL);")]
    public void SerialColumnTypesAreRejected(string sql)
    {
        ExpectSyntaxError(sql, _schema);
    }

    // Live Netezza 11.2 rejects CLOB(n)/NCLOB(n) and bare BLOB as column
    // typenames (evidence-campaign 20261018f, dotnet-template.112).
    [Theory]
    [InlineData("CREATE TABLE LOB_TYPES (DATA CLOB(1000), NDATA NCLOB(500), RAW BLOB);")]
    [InlineData("CREATE TABLE T1 (DATA CLOB(1000));")]
    [InlineData("CREATE TABLE T1 (DATA NCLOB(500));")]
    [InlineData("CREATE TABLE T1 (RAW BLOB);")]
    public void LobColumnTypeShapesAreRejected(string sql)
    {
        ExpectSyntaxError(sql, _schema);
    }

    [Theory]
    [InlineData("CREATE TABLE T1 (DATA CLOB);")]
    [InlineData("CREATE TABLE T1 (DATA NCLOB);")]
    [InlineData("CREATE TABLE T1 (NAME VARCHAR(100));")]
    [InlineData("SELECT CAST(1 AS VARCHAR(10));")]
    public void OtherTypenameShapesRemainValid(string sql)
    {
        ExpectValid(sql, _schema);
    }
}
