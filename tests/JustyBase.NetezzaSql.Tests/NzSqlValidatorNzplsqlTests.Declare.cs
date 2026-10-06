using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Visitor;
using static JustyBase.Tests.NetezzaSqlParser.SqlTestHelpers;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed partial class NzSqlValidatorNzplsqlTests
{
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
    public void Validate_Variables_AmpersandVariable()
    {
        ExpectValid("SELECT &myVar;");
    }


    [Fact]
    public void Validate_Variables_AmpersandVariableInLimit()
    {
        ExpectValid(
            "SELECT STATUS FROM TESTDB.PUBLIC.ORDERS WHERE STATUS LIKE &searched LIMIT &limit OFFSET &offset;",
            _schema);
    }


    [Fact]
    public void Validate_Variables_AmpersandVariableAsTableName()
    {
        ExpectValid("SELECT &column FROM &table;", _schema);
    }


    [Fact]
    public void Validate_Variables_AmpersandVariableInCompositeName()
    {
        ExpectValid("SELECT NAME_&suffix FROM TESTDB.PUBLIC.ORDERS;", _schema);
    }


    [Fact]
    public void Validate_Variables_AmpersandVariableInDdlName()
    {
        ExpectValid("CREATE TABLE NAME_&suffix (ID INT4);", _schema);
    }


    [Fact]
    public void Validate_Variables_AmpersandVariableIsNotColumnWithSchema()
    {
        ExpectValid("SELECT STATUS FROM TESTDB.PUBLIC.ORDERS WHERE STATUS LIKE &searched;", _schema);
    }


    [Fact]
    public void Validate_Variables_SingleSemicolon()
    {
        ExpectValid(";");
    }
}
