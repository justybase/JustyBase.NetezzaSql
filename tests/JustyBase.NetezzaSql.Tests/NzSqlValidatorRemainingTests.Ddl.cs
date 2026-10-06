using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;
using JustyBase.NetezzaSqlParser.Visitor;
using static JustyBase.Tests.NetezzaSqlParser.SqlTestHelpers;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed partial class NzSqlValidatorRemainingTests
{
    [Fact]
    public void Validate_EdgeCases_DatabaseTableNotation()
    {
        ExpectValid("SELECT * FROM TESTDB..EMPLOYEES;", _schema);
    }


    [Fact]
    public void Validate_EdgeCases_DatabaseSchemaTableNotation()
    {
        ExpectValid("SELECT * FROM TESTDB.PUBLIC.EMPLOYEES;", _schema);
    }


    [Fact]
    public void Validate_EdgeCases_MixedCommentStyles()
    {
        ExpectValid(
            """
            -- Line comment at start
            SELECT
                E.EMPLOYEE_ID, /* block comment inline */
                E.FIRST_NAME -- trailing comment
            FROM TESTDB..EMPLOYEES E
            /* multi-line
               block comment */
            WHERE E.SALARY > 0;
            """,
            _schema);
    }


    [Fact]
    public void Validate_Utility_GroomTable()
    {
        ExpectValid("GROOM TABLE TESTDB..EMPLOYEES;", _schema);
    }


    [Fact]
    public void Validate_Utility_GroomTableVersions()
    {
        ExpectValid("GROOM TABLE TESTDB..EMPLOYEES VERSIONS;", _schema);
    }


    [Fact]
    public void Validate_Utility_CommentOnTable()
    {
        ExpectValid(
            "COMMENT ON TABLE TESTDB..EMPLOYEES IS 'Employee master table';",
            _schema);
    }


    [Fact]
    public void Validate_Utility_CommentOnColumn()
    {
        ExpectValid(
            "COMMENT ON COLUMN TESTDB..EMPLOYEES.SALARY IS 'Monthly salary in USD';",
            _schema);
    }


    [Fact]
    public void Validate_Utility_LockTable()
    {
        ExpectValid("LOCK TABLE TESTDB..EMPLOYEES IN EXCLUSIVE MODE;", _schema);
    }


    [Fact]
    public void Validate_Alter_TableDropColumn()
    {
        // Live Netezza requires RESTRICT or CASCADE (live evidence 2026-10-06).
        ExpectSyntaxError("ALTER TABLE TESTDB..EMPLOYEES DROP COLUMN STATUS;", _schema);
        ExpectValid("ALTER TABLE TESTDB..EMPLOYEES DROP COLUMN STATUS RESTRICT;", _schema);
    }


    [Fact]
    public void Validate_Alter_TableOwnerTo()
    {
        ExpectValid("ALTER TABLE TESTDB..EMPLOYEES OWNER TO ADMIN;", _schema);
    }


    [Fact]
    public void Validate_Alter_ViewOwnerTo()
    {
        ExpectValid("ALTER VIEW TESTDB..EMP_VIEW OWNER TO ADMIN;", _schema);
    }


    [Fact]
    public void Validate_Alter_DatabaseRenameTo()
    {
        ExpectValid("ALTER DATABASE TESTDB RENAME TO NEWDB;");
    }

    // ========================================================================
    // Advanced grammar coverage — simple subset
    // ========================================================================

    [Fact]
    public void Validate_Advanced_SimpleUnion()
    {
        ExpectValid("SELECT 1 AS A UNION SELECT 2 AS A;");
    }


    [Fact]
    public void Validate_Call_ExecuteProcedureAlternative()
    {
        ExpectValid("EXECUTE PROCEDURE SOME_PROC_NAME()");
    }


    [Fact]
    public void Validate_Call_ExecuteAlternative()
    {
        ExpectValid("EXECUTE SOME_PROC_NAME()");
    }


    [Fact]
    public void Validate_NodeParity_CreatexidSystemColumn()
    {
        ExpectValid(
            "SELECT CREATEXID FROM TESTDB..EMPLOYEES;",
            _schema);
    }


    [Fact]
    public void Validate_NodeParity_DropTableIfExists()
    {
        // Live Netezza rejects DROP IF EXISTS (live evidence 2026-10-06).
        ExpectSyntaxError("DROP TABLE IF EXISTS TESTDB..EMPLOYEES;", _schema);
    }


    [Fact]
    public void Validate_NodeParity_CreateTableIfNotExists()
    {
        ExpectValid(
            "CREATE TABLE IF NOT EXISTS TESTDB..NEW_TABLE (ID INT4, NAME VARCHAR(100));",
            _schema);
    }


    [Fact]
    public void Validate_NodeParity_DbTableFormDetection()
    {
        // DB.TABLE (single dot) should produce SQL007
        ExpectErrorCode(
            "SELECT * FROM TESTDB.EMPLOYEES;",
            "SQL007",
            _schema);
    }


    [Fact]
    public void Validate_NodeParity_NextValueForSequence()
    {
        ExpectValid("SELECT NEXT VALUE FOR MY_SEQ;");
    }


    [Fact]
    public void Validate_NodeParity_CommentOnTable()
    {
        ExpectValid("COMMENT ON TABLE TESTDB..EMPLOYEES IS 'Employee master table';", _schema);
    }


    [Fact]
    public void Validate_NodeParity_CommentOnColumn()
    {
        ExpectValid("COMMENT ON COLUMN TESTDB..EMPLOYEES.SALARY IS 'Annual salary';", _schema);
    }


    [Fact]
    public void Validate_NodeParity_GroomTableVersions()
    {
        ExpectValid("GROOM TABLE TESTDB..EMPLOYEES VERSIONS;", _schema);
    }


    [Fact]
    public void Validate_NodeParity_GroomTableRecordsReady()
    {
        ExpectValid("GROOM TABLE TESTDB..EMPLOYEES RECORDS READY;", _schema);
    }


    [Fact]
    public void Validate_NodeParity_GroomTableReclaimBackupsetDefault()
    {
        ExpectValid("GROOM TABLE TESTDB..EMPLOYEES RECORDS ALL RECLAIM BACKUPSET DEFAULT;", _schema);
    }
}
