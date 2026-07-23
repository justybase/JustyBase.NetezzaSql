using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Visitor;
using static JustyBase.Tests.NetezzaSqlParser.SqlTestHelpers;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class NzSqlValidatorNzplsqlTests
{
    private readonly ISchemaProvider _schema = CreateStandardMockSchema();

    // ========================================================================
    // Stored Procedures — valid syntax
    // ========================================================================

    [Fact]
    public void Validate_CreateProcedure_Minimal()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE MY_PROC()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                RETURN 1;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_CreateProcedure_ExecuteAsOwner()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE MY_PROC()
            RETURNS INT4
            EXECUTE AS OWNER
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                RETURN 0;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_CreateProcedure_ExecuteAsCaller()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE MY_PROC()
            RETURNS INT4
            EXECUTE AS CALLER
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                RETURN 0;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_CreateProcedure_ExecuteAsBeforeReturns()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE PROCEDURE_NAME(INTEGER, VARCHAR(100))
            EXECUTE AS OWNER
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                RETURN 0;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_CreateProcedure_TypedParameters()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE ADD_NUMS(p_a INT4, p_b INT4)
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                RETURN p_a + p_b;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_CreateProcedure_Varargs()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE VARARG_PROC(VARARGS)
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                RETURN 0;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_CreateProcedure_DecVarWithInit()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE VAR_PROC()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_count INT4 := 0;
                v_name VARCHAR(50) := 'default';
                v_flag BOOLEAN;
            BEGIN
                v_count := v_count + 1;
                RETURN v_count;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_CreateProcedure_ConstantVariable()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE CONST_PROC()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_pi CONSTANT NUMERIC(10,5) := 3.14159;
            BEGIN
                RETURN 1;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_CreateProcedure_NotNullVariable()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE NOTNULL_PROC()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_id INT4 NOT NULL := 1;
            BEGIN
                RETURN v_id;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_CreateProcedure_IfElsifElse()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE IF_PROC(p_val INT4)
            RETURNS VARCHAR(20)
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                IF p_val > 100 THEN
                    RETURN 'High';
                ELSIF p_val > 50 THEN
                    RETURN 'Medium';
                ELSE
                    RETURN 'Low';
                END IF;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_CreateProcedure_WhileLoop()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE WHILE_PROC()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_i INT4 := 0;
            BEGIN
                WHILE v_i < 10 LOOP
                    v_i := v_i + 1;
                END LOOP;
                RETURN v_i;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_CreateProcedure_LoopWithExit()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE LOOP_PROC()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_i INT4 := 0;
            BEGIN
                LOOP
                    v_i := v_i + 1;
                    EXIT WHEN v_i >= 5;
                END LOOP;
                RETURN v_i;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_CreateProcedure_ForRangeLoop()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE FOR_RANGE_PROC()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_sum INT4 := 0;
            BEGIN
                FOR i IN 1..10 LOOP
                    v_sum := v_sum + i;
                END LOOP;
                RETURN v_sum;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_CreateProcedure_ForSelectLoop()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE FOR_QUERY_PROC()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_cnt INT4 := 0;
            BEGIN
                FOR rec IN SELECT EMPLOYEE_ID FROM TESTDB..EMPLOYEES LIMIT 5 LOOP
                    v_cnt := v_cnt + 1;
                END LOOP;
                RETURN v_cnt;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_CreateProcedure_ForExecuteLoop()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE FOR_DYN_PROC()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_cnt INT4 := 0;
            BEGIN
                FOR rec IN EXECUTE 'SELECT 1 AS X' LOOP
                    v_cnt := v_cnt + 1;
                END LOOP;
                RETURN v_cnt;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_CreateProcedure_ExceptionWhenOthers()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE EX_PROC()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                RETURN 1;
            EXCEPTION
                WHEN OTHERS THEN
                    RAISE NOTICE 'Error occurred';
                    RETURN 0;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_CreateProcedure_RaiseLevels()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE RAISE_PROC()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                RAISE DEBUG 'debug message';
                RAISE NOTICE 'notice message %', 42;
                RAISE EXCEPTION 'critical error';
                RETURN 1;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_CreateProcedure_ExecuteImmediate()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE DYN_PROC()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_sql TEXT;
            BEGIN
                v_sql := 'SELECT 1';
                EXECUTE IMMEDIATE v_sql;
                RETURN 1;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_CreateProcedure_ExecuteImmediateUsing()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE DYN_USING_PROC()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                EXECUTE IMMEDIATE 'UPDATE TESTDB..FILMS SET KIND = ? WHERE CODE = ?' USING 'Drama', 'AA001';
                RETURN 1;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_CreateProcedure_CallStatement()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE CALLER_PROC()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                CALL MY_PROC();
                RETURN 1;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_CreateProcedure_ExecuteProcedure()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE EXEC_PROC()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                EXECUTE PROCEDURE MY_PROC();
                RETURN 1;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_CreateProcedure_RollbackCommit()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE TX_PROC()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                ROLLBACK;
                COMMIT;
                RETURN 1;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_CreateProcedure_ReturnsRefTable()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE RT_PROC()
            RETURNS REFTABLE(TESTDB.PUBLIC.EMPLOYEES)
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                RETURN REFTABLE;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_CreateProcedure_VarrayVariable()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE ARR_PROC()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                arr VARRAY(10) OF INT4;
                v_val INT4;
            BEGIN
                arr(1) := 100;
                v_val := 42;
                RETURN v_val;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_CreateProcedure_RecordVariable()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE REC_PROC()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                rec RECORD;
            BEGIN
                RETURN 1;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_CreateProcedure_NestedBlocks()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE NESTED_PROC()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_outer INT4 := 0;
            BEGIN
                DECLARE
                    v_inner INT4 := 10;
                BEGIN
                    v_outer := v_inner;
                END;
                RETURN v_outer;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_CreateProcedure_DmlInsideBody()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE DML_PROC()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                INSERT INTO TESTDB..FILMS (CODE, TITLE) VALUES ('ZZ', 'Test');
                UPDATE TESTDB..FILMS SET TITLE = 'Updated' WHERE CODE = 'ZZ';
                DELETE FROM TESTDB..FILMS WHERE CODE = 'ZZ';
                RETURN 1;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_CreateProcedure_DropTableInside()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE DROP_PROC()
            RETURNS INT4
            EXECUTE AS OWNER
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                RAISE NOTICE 'Before drop';
                DROP TABLE TESTDB..TMP_TO_DROP;
                RETURN 1;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_CreateProcedure_DdlChain()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE DDL_CHAIN_PROC()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                CREATE TEMP TABLE TMP_PROC_T (ID INT4, NAME VARCHAR(20));
                ALTER TABLE TMP_PROC_T ADD COLUMN FLAG CHAR(1);
                COMMENT ON TABLE TMP_PROC_T IS 'Temporary table for procedure flow';
                DROP TABLE TMP_PROC_T;
                RETURN 1;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_CreateProcedure_CreateDropView()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE VIEW_CHAIN_PROC()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                CREATE VIEW TESTDB..V_PROC_TMP AS SELECT EMPLOYEE_ID FROM TESTDB..EMPLOYEES;
                DROP VIEW TESTDB..V_PROC_TMP;
                RETURN 1;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_CreateProcedure_MaintenanceCommands()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE MAINT_PROC()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                GROOM TABLE TESTDB..EMPLOYEES RECORDS ALL;
                GENERATE STATISTICS ON TESTDB..EMPLOYEES;
                RETURN 1;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_CreateProcedure_TruncateDirect()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE TRUNC_PROC()
            RETURNS INTEGER
            EXECUTE AS OWNER
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            TRUNCATE TABLE XYZ;
            RETURN 1;
            END_PROC;");
    }

    [Fact]
    public void Validate_CreateProcedure_TruncateFullName()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE JUST_DATA.ADMIN.TEST_PROC()
            RETURNS INTEGER
            EXECUTE AS OWNER
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            TRUNCATE TABLE XYZ;
            RETURN 0;
            END_PROC;");
    }

    [Fact]
    public void Validate_CreateProcedure_GrantInBody()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE GRANT_PROC()
            RETURNS INTEGER
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                GRANT SELECT ON TESTDB..EMPLOYEES TO PUBLIC;
                RETURN 1;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_CreateProcedure_RevokeInBody()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE REVOKE_PROC()
            RETURNS INTEGER
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                REVOKE SELECT ON TESTDB..EMPLOYEES FROM PUBLIC;
                RETURN 1;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_CreateProcedure_IsInsteadOfAs()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE IS_PROC()
            RETURNS INT4
            LANGUAGE NZPLSQL IS
            BEGIN_PROC
            BEGIN
                RETURN 1;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_CreateProcedure_MultipleStatements()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE MULTI_STMT_PROC()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_a INT4 := 1;
                v_b INT4 := 2;
                v_c INT4;
            BEGIN
                v_c := v_a + v_b;
                v_a := v_c * 2;
                RETURN v_a;
            END;
            END_PROC;");
    }

    // ========================================================================
    // Stored Procedures — syntax errors
    // ========================================================================

    [Fact]
    public void Validate_Procedure_SyntaxError_MissingReturns()
    {
        ExpectSyntaxError(@"
            CREATE OR REPLACE PROCEDURE BAD_PROC()
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                RETURN 1;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Procedure_SyntaxError_MissingLanguage()
    {
        ExpectSyntaxError(@"
            CREATE OR REPLACE PROCEDURE BAD_PROC()
            RETURNS INT4
            NZPLSQL AS
            BEGIN_PROC
            BEGIN
                RETURN 1;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Procedure_SyntaxError_MissingBeginProc()
    {
        ExpectSyntaxError(@"
            CREATE OR REPLACE PROCEDURE BAD_PROC()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN
                RETURN 1;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Procedure_SyntaxError_MissingEndProc()
    {
        ExpectSyntaxError(@"
            CREATE OR REPLACE PROCEDURE BAD_PROC()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                RETURN 1;
            END;");
    }

    [Fact]
    public void Validate_Procedure_SyntaxError_MissingEndIf()
    {
        ExpectSyntaxError(@"
            CREATE OR REPLACE PROCEDURE BAD_IF()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                IF 1 = 1 THEN
                    RETURN 1;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Procedure_SyntaxError_MissingThenInIf()
    {
        ExpectSyntaxError(@"
            CREATE OR REPLACE PROCEDURE BAD_IF2()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                IF 1 = 1
                    RETURN 1;
                END IF;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Procedure_SyntaxError_MissingEndLoopInWhile()
    {
        ExpectSyntaxError(@"
            CREATE OR REPLACE PROCEDURE BAD_WHILE()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_i INT4 := 0;
            BEGIN
                WHILE v_i < 10 LOOP
                    v_i := v_i + 1;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Procedure_SyntaxError_MissingLoopInWhile()
    {
        ExpectSyntaxError(@"
            CREATE OR REPLACE PROCEDURE BAD_WHILE2()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_i INT4 := 0;
            BEGIN
                WHILE v_i < 10
                    v_i := v_i + 1;
                END LOOP;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Procedure_SyntaxError_MissingEndLoopInFor()
    {
        ExpectSyntaxError(@"
            CREATE OR REPLACE PROCEDURE BAD_FOR()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                FOR i IN 1..5 LOOP
                    RAISE NOTICE 'i=%', i;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Procedure_SyntaxError_RaiseWithoutSeverity()
    {
        ExpectSyntaxError(@"
            CREATE OR REPLACE PROCEDURE BAD_RAISE()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                RAISE 'missing severity';
                RETURN 1;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Procedure_SyntaxError_MissingOpenParenArgs()
    {
        ExpectSyntaxError(@"
            CREATE OR REPLACE PROCEDURE BAD_ARGS p_id INT4)
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                RETURN 1;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Procedure_SyntaxError_MissingCloseParenArgs()
    {
        ExpectSyntaxError(@"
            CREATE OR REPLACE PROCEDURE BAD_ARGS(p_id INT4
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                RETURN 1;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Procedure_SyntaxError_MissingBeginInside()
    {
        ExpectSyntaxError(@"
            CREATE OR REPLACE PROCEDURE NO_BEGIN()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_i INT4 := 0;
                RETURN v_i;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Procedure_SyntaxError_MissingEndInside()
    {
        ExpectSyntaxError(@"
            CREATE OR REPLACE PROCEDURE NO_END()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                RETURN 1;
            END_PROC;");
    }

    // ========================================================================
    // Stored Procedures — semantic errors
    // ========================================================================

    [Fact]
    public void Validate_Procedure_SemanticError_InvalidDataType()
    {
        ExpectErrorCode(@"
            CREATE OR REPLACE PROCEDURE BAD_TYPE_PROC()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_bad INVALID_TYPE_XYZ;
            BEGIN
                RETURN 1;
            END;
            END_PROC;",
            "SQL013", _schema);
    }

    [Fact]
    public void Validate_Procedure_SemanticError_UnknownFunction()
    {
        ExpectErrorCode(@"
            CREATE OR REPLACE PROCEDURE BAD_FN_PROC()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_x INT4;
            BEGIN
                v_x := NONEXISTENT_FUNC_99(1, 2);
                RETURN v_x;
            END;
            END_PROC;",
            "SQL011", _schema);
    }

    // ========================================================================
    // NZPLSQL — variable type validation
    // ========================================================================

    [Fact]
    public void Validate_Variable_Int4()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE INT4_VAR()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_int INT4 := 42;
            BEGIN
                RETURN v_int;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Variable_Varchar()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE VARCHAR_VAR()
            RETURNS VARCHAR(100)
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_name VARCHAR(100) := 'test';
            BEGIN
                RETURN v_name;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Variable_Numeric()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE NUMERIC_VAR()
            RETURNS NUMERIC(10,2)
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_amount NUMERIC(10,2) := 123.45;
            BEGIN
                RETURN v_amount;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Variable_Boolean()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE BOOL_VAR()
            RETURNS BOOL
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_flag BOOL := TRUE;
            BEGIN
                RETURN v_flag;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Variable_Date()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE DATE_VAR()
            RETURNS DATE
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_date DATE;
            BEGIN
                RETURN v_date;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Variable_Timestamp()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE TS_VAR()
            RETURNS TIMESTAMP
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_ts TIMESTAMP;
            BEGIN
                RETURN v_ts;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Variable_UnknownType()
    {
        ExpectErrorCode(@"
            CREATE OR REPLACE PROCEDURE BAD_TYPE()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_bad FOOBAR_TYPE;
            BEGIN
                RETURN 1;
            END;
            END_PROC;",
            "SQL013", _schema);
    }

    [Fact]
    public void Validate_Variable_AssignmentOperator()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE ASSIGN_VAR()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_count INT4 := 0;
            BEGIN
                RETURN v_count;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Variable_Constant()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE CONST_VAR()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_max CONSTANT INT4 := 100;
            BEGIN
                RETURN v_max;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Variable_NotNull()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE NOTNULL_VAR()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_id INT4 NOT NULL := 1;
            BEGIN
                RETURN v_id;
            END;
            END_PROC;");
    }

    // ========================================================================
    // NZPLSQL — RETURN statement validation
    // ========================================================================

    [Fact]
    public void Validate_Return_Literal()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE RET_LITERAL()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                RETURN 42;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Return_Variable()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE RET_VAR()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_result INT4 := 100;
            BEGIN
                RETURN v_result;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Return_Expression()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE RET_EXPR()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_a INT4 := 10;
                v_b INT4 := 20;
            BEGIN
                RETURN v_a + v_b;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Return_FunctionCall()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE RET_FUNC()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                RETURN COALESCE(NULL, 1);
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Return_RefTable()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE RET_REFTABLE()
            RETURNS REFTABLE(TESTDB.PUBLIC.EMPLOYEES)
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                RETURN REFTABLE;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Return_MultipleBranches()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE RET_BRANCHES(p_val INT4)
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                IF p_val > 0 THEN
                    RETURN 1;
                ELSIF p_val < 0 THEN
                    RETURN -1;
                ELSE
                    RETURN 0;
                END IF;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Return_NestedBlocks()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE RET_NESTED()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_outer INT4 := 1;
            BEGIN
                DECLARE
                    v_inner INT4 := 2;
                BEGIN
                    IF v_inner > 0 THEN
                        RETURN v_inner;
                    END IF;
                END;
                RETURN v_outer;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Return_StringLiteral()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE RET_STR()
            RETURNS VARCHAR(50)
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                RETURN 'Hello World';
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Return_BooleanExpression()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE RET_BOOL()
            RETURNS BOOL
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                RETURN 1 = 1;
            END;
            END_PROC;");
    }

    // ========================================================================
    // NZPLSQL — parameter validation
    // ========================================================================

    [Fact]
    public void Validate_Parameter_Single()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE SINGLE_PARAM(p_id INT4)
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                RETURN p_id;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Parameter_Multiple()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE MULTI_PARAM(p_a INT4, p_b VARCHAR(50), p_c NUMERIC(10,2))
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                RETURN p_a;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Parameter_Varargs()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE VARARGS_PROC(VARARGS)
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                RETURN 0;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Parameter_AliasFor()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE ALIAS_PROC(INT4, VARCHAR(100))
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                p_id ALIAS FOR $1;
                p_name ALIAS FOR $2;
            BEGIN
                RETURN p_id;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Parameter_InExpressions()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE PARAM_EXPR(p_base INT4, p_multiplier INT4)
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                RETURN p_base * p_multiplier;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Parameter_InSqlStatements()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE PARAM_SQL(p_dept_id INT4)
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_count INT4;
            BEGIN
                SELECT COUNT(*) INTO v_count FROM TESTDB..EMPLOYEES WHERE DEPARTMENT_ID = p_dept_id;
                RETURN v_count;
            END;
            END_PROC;", _schema);
    }

    // ========================================================================
    // Stored Procedures — additional valid patterns
    // ========================================================================

    [Fact]
    public void Validate_Additional_IfElsifChain()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE IF_TEST(p_val INT4)
            RETURNS VARCHAR(50)
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_result VARCHAR(50);
            BEGIN
                IF p_val > 100 THEN
                    v_result := 'High';
                ELSIF p_val > 50 THEN
                    v_result := 'Medium';
                ELSIF p_val > 10 THEN
                    v_result := 'Low';
                ELSE
                    v_result := 'Very Low';
                END IF;
                RETURN v_result;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Additional_NestedIf()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE NESTED_IF(p_a INT4, p_b INT4)
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_result INT4;
            BEGIN
                IF p_a > 0 THEN
                    IF p_b > 0 THEN
                        v_result := 1;
                    ELSE
                        v_result := 2;
                    END IF;
                ELSE
                    v_result := 3;
                END IF;
                RETURN v_result;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Additional_ForLoop()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE FOR_LOOP_TEST()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_sum INT4 := 0;
                i INT4;
            BEGIN
                FOR i IN 1..10 LOOP
                    v_sum := v_sum + i;
                END LOOP;
                RETURN v_sum;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Additional_RaiseNotice()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE RAISE_TEST()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                RAISE NOTICE 'Starting procedure';
                RAISE NOTICE 'Value is: %', 42;
                RETURN 0;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Additional_RaiseDebug()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE DEBUG_TEST()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                RAISE DEBUG 'Debug message';
                RETURN 0;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Additional_RaiseException()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE EXCEPTION_TEST(p_val INT4)
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                IF p_val < 0 THEN
                    RAISE EXCEPTION 'Value must be non-negative: %', p_val;
                END IF;
                RETURN p_val;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Additional_ExecuteImmediate()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE DYN_SQL_TEST()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_sql VARCHAR(500);
            BEGIN
                v_sql := 'SELECT COUNT(*) FROM EMPLOYEES';
                EXECUTE IMMEDIATE v_sql;
                RETURN 0;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Additional_ExitWhenLoop()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE EXIT_LOOP_TEST()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_counter INT4 := 0;
            BEGIN
                LOOP
                    v_counter := v_counter + 1;
                    EXIT WHEN v_counter >= 10;
                END LOOP;
                RETURN v_counter;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Additional_WhileComplex()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE WHILE_COMPLEX()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_i INT4 := 0;
                v_j INT4 := 100;
            BEGIN
                WHILE v_i < 10 AND v_j > 0 LOOP
                    v_i := v_i + 1;
                    v_j := v_j - 10;
                END LOOP;
                RETURN v_i;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Additional_MultipleReturnPaths()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE MULTI_RETURN(p_val INT4)
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                IF p_val > 0 THEN
                    RETURN 1;
                END IF;
                IF p_val < 0 THEN
                    RETURN -1;
                END IF;
                RETURN 0;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Additional_ReturnBool()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE BOOL_PROC(p_val INT4)
            RETURNS BOOL
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                IF p_val > 0 THEN
                    RETURN TRUE;
                END IF;
                RETURN FALSE;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Additional_NamedParameters()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE NAMED_PARAMS(p_name VARCHAR(100), p_age INT4, p_salary NUMERIC(10,2))
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                RETURN 0;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Additional_MultiVariableDeclarations()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE MULTI_VARS()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v_int INT4 := 0;
                v_str VARCHAR(100) := 'hello';
                v_bool BOOL := TRUE;
                v_num NUMERIC(10,2) := 3.14;
            BEGIN
                RETURN v_int;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Additional_CallAnotherProcedure()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE CALLER_PROC()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                CALL NAMED_PARAMS('test', 25, 50000.00);
                RETURN 0;
            END;
            END_PROC;");
    }

    // ========================================================================
    // Stored Procedures — additional syntax errors
    // ========================================================================

    [Fact]
    public void Validate_Additional_Syntax_MissingReturns()
    {
        ExpectSyntaxError(@"
            CREATE OR REPLACE PROCEDURE BAD_PROC()
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                RETURN 0;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Additional_Syntax_MissingLanguage()
    {
        ExpectSyntaxError(@"
            CREATE OR REPLACE PROCEDURE BAD_PROC()
            RETURNS INT4
            AS
            BEGIN_PROC
            BEGIN
                RETURN 0;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Additional_Syntax_MissingEndProc()
    {
        ExpectSyntaxError(@"
            CREATE OR REPLACE PROCEDURE BAD_PROC()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                RETURN 0;
            END;");
    }

    [Fact]
    public void Validate_Additional_Syntax_MissingEnd()
    {
        ExpectSyntaxError(@"
            CREATE OR REPLACE PROCEDURE BAD_PROC()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                RETURN 0;
            END_PROC;");
    }

    [Fact]
    public void Validate_Additional_Syntax_MissingSemicolons()
    {
        ExpectSyntaxError(@"
            CREATE OR REPLACE PROCEDURE BAD_PROC()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v INT4;
            BEGIN
                v := 1
                RETURN v;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Additional_Syntax_MissingLoopAfterWhile()
    {
        ExpectSyntaxError(@"
            CREATE OR REPLACE PROCEDURE BAD_WHILE()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                v INT4 := 0;
            BEGIN
                WHILE v < 10
                    v := v + 1;
                END LOOP;
                RETURN v;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Additional_Syntax_MissingThenAfterIf()
    {
        ExpectSyntaxError(@"
            CREATE OR REPLACE PROCEDURE BAD_IF()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                IF 1 > 0
                    RETURN 1;
                END IF;
                RETURN 0;
            END;
            END_PROC;");
    }

    // ========================================================================
    // NZPLSQL advanced features
    // ========================================================================

    [Fact]
    public void Validate_Advanced_AutocommitOn()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE TESTDB.PUBLIC.P_AUTOCOMMIT()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            BEGIN
                AUTOCOMMIT ON;
                RETURN 1;
            END;
            END_PROC;");
    }

    [Fact]
    public void Validate_Advanced_RecordFieldAssignment()
    {
        ExpectValid(@"
            CREATE OR REPLACE PROCEDURE TESTDB.PUBLIC.P_REC_FIELD()
            RETURNS INT4
            LANGUAGE NZPLSQL AS
            BEGIN_PROC
            DECLARE
                rec RECORD;
            BEGIN
                rec.employee_id := 1;
                RETURN 1;
            END;
            END_PROC;");
    }

    // ========================================================================
    // Variables and basic statements
    // ========================================================================

    [Fact]
    public void Validate_Variables_Commit()
    {
        ExpectValid("COMMIT;");
    }

    [Fact]
    public void Validate_Variables_Rollback()
    {
        ExpectValid("ROLLBACK;");
    }

    [Fact]
    public void Validate_Variables_AtSet()
    {
        ExpectValid("@SET myVar = 1;");
    }

    [Fact]
    public void Validate_Variables_SetCatalog()
    {
        ExpectValid("SET CATALOG JUST_DATA;");
    }

    [Fact]
    public void Validate_Variables_DollarVariable()
    {
        ExpectValid("SELECT $myVar;");
    }

    [Fact]
    public void Validate_Variables_BracedVariable()
    {
        ExpectValid("SELECT ${myVar};");
    }

    [Fact]
    public void Validate_Variables_SingleSemicolon()
    {
        ExpectValid(";");
    }
}
