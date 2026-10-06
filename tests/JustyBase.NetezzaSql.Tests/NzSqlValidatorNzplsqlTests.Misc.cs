using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Visitor;
using static JustyBase.Tests.NetezzaSqlParser.SqlTestHelpers;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed partial class NzSqlValidatorNzplsqlTests
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
    public void Validate_CreateProcedure_IsInsteadOfAs_IsRejected()
    {
        // Netezza requires AS after the procedure signature (procedure matrix hdr_is_rejected).
        ExpectSyntaxError(@"
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
}
