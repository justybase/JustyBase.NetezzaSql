using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Visitor;
using static JustyBase.Tests.NetezzaSqlParser.SqlTestHelpers;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed partial class NzSqlValidatorNzplsqlTests
{
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
}
