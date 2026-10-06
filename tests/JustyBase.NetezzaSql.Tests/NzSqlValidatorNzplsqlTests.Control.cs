using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Visitor;
using static JustyBase.Tests.NetezzaSqlParser.SqlTestHelpers;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed partial class NzSqlValidatorNzplsqlTests
{
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
}
