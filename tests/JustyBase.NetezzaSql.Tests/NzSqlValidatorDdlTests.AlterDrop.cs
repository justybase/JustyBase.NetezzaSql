using JustyBase.NetezzaSqlParser.Visitor;
using static JustyBase.Tests.NetezzaSqlParser.SqlTestHelpers;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed partial class NzSqlValidatorDdlTests
{
    [Fact]
    public void DropView()
    {
        ExpectValid("DROP VIEW V_EMP;", _schema);
    }


    [Fact]
    public void DropProcedure()
    {
        ExpectValid("DROP PROCEDURE MY_PROC;", _schema);
    }


    [Fact]
    public void DropDatabase()
    {
        ExpectValid("DROP DATABASE OLD_DB;", _schema);
    }


    [Fact]
    public void DropSequence()
    {
        ExpectValid("DROP SEQUENCE TESTDB.PUBLIC.SEQ_1;", _schema);
    }


    [Fact]
    public void DropSynonym()
    {
        ExpectValid("DROP SYNONYM TESTDB.PUBLIC.SYN_1;", _schema);
    }




    [Fact]
    public void AlterDatabase_OwnerTo()
    {
        ExpectValid("ALTER DATABASE TESTDB OWNER TO ADMIN;", _schema);
    }


    [Fact]
    public void AlterSequence_RestartWith()
    {
        ExpectValid("ALTER SEQUENCE TESTDB.PUBLIC.SEQ_1 RESTART WITH 1;", _schema);
    }


    [Fact]
    public void AlterUser_WithPassword()
    {
        ExpectValid("ALTER USER APP_USER WITH PASSWORD 'newpass';", _schema);
    }


    [Fact]
    public void AlterView_RenameTo()
    {
        ExpectValid("ALTER VIEW TESTDB.PUBLIC.V_EMP RENAME TO V_EMPLOYEES;", _schema);
    }




    [Fact]
    public void DropView_Multiple()
    {
        ExpectValid("DROP VIEW v1, v2", _schema);
    }


    [Fact]
    public void DropSequence_Simple()
    {
        ExpectValid("DROP SEQUENCE my_seq", _schema);
    }


    [Fact]
    public void DropSchema_Cascade()
    {
        ExpectValid("DROP SCHEMA mydb.myschema CASCADE", _schema);
    }


    [Fact]
    public void DropSchema_Restrict()
    {
        ExpectValid("DROP SCHEMA myschema RESTRICT", _schema);
    }


    [Fact]
    public void DropSynonym_Simple()
    {
        ExpectValid("DROP SYNONYM my_syn", _schema);
    }


    [Fact]
    public void DropSession()
    {
        ExpectValid("DROP SESSION 12345", _schema);
    }


    [Fact]
    public void DropUser()
    {
        ExpectValid("DROP USER testuser", _schema);
    }


    [Fact]
    public void DropProcedure_Simple()
    {
        ExpectValid("DROP PROCEDURE my_proc", _schema);
    }


    [Fact]
    public void DropDatabase_Simple()
    {
        ExpectValid("DROP DATABASE test_db", _schema);
    }


    [Fact]
    public void DropGroup()
    {
        ExpectValid("DROP GROUP dev_team", _schema);
    }




    [Fact]
    public void Drop_WithoutObjectType()
    {
        ExpectSyntaxError("DROP TESTDB..EMPLOYEES;", _schema);
    }


    [Fact]
    public void AlterView_OwnerTo()
    {
        ExpectValid("ALTER VIEW TESTDB..EMP_VIEW OWNER TO ADMIN;", _schema);
    }


    [Fact]
    public void AlterDatabase_RenameTo()
    {
        ExpectValid("ALTER DATABASE TESTDB RENAME TO NEWDB;", _schema);
    }
}
