using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Visitor;
using static JustyBase.Tests.NetezzaSqlParser.SqlTestHelpers;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed partial class NzSqlValidatorNzplsqlTests
{
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
}
