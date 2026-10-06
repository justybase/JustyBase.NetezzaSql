using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;
using JustyBase.NetezzaSqlParser.Visitor;
using static JustyBase.Tests.NetezzaSqlParser.SqlTestHelpers;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed partial class NzSqlValidatorRemainingTests
{
    [Fact]
    public void Validate_Utility_MergeCommand()
    {
        ExpectValid(
            "MERGE INTO TESTDB..EMPLOYEES E USING TESTDB..DEPARTMENTS D ON E.DEPARTMENT_ID = D.DEPARTMENT_ID WHEN MATCHED THEN UPDATE SET STATUS = 'A';",
            _schema);
    }


    [Fact]
    public void Validate_Advanced_DeleteWithExists()
    {
        ExpectValid(
            "DELETE FROM TESTDB..EMPLOYEES E WHERE EXISTS (SELECT 1 FROM TESTDB..ORDERS O WHERE O.CUSTOMER_ID = E.EMPLOYEE_ID);",
            _schema);
    }


    [Fact]
    public void Validate_NodeParity_DeletexidSystemColumn()
    {
        ExpectValid(
            "SELECT DELETEXID FROM TESTDB..EMPLOYEES;",
            _schema);
    }


    [Fact]
    public void Validate_NodeParity_DeleteWithExists()
    {
        ExpectValid(
            "DELETE FROM TESTDB..EMPLOYEES E WHERE EXISTS (SELECT 1 FROM TESTDB..DEPARTMENTS D WHERE D.DEPARTMENT_ID = E.DEPARTMENT_ID);",
            _schema);
    }


    [Fact]
    public void Validate_NodeParity_TruncateWithoutTableKeyword()
    {
        ExpectValid("TRUNCATE TESTDB..EMPLOYEES;", _schema);
    }
}
